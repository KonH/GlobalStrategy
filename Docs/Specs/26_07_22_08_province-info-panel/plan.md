# Plan: Province Info Panel

## Spec

Source: `Docs/Specs/26_07_22_08_province-info-panel/spec.md`.

**Intent.** Add a selected-province info panel, visible only in the Province map lens, that shows the province's localized name, its owning country (plus, when occupied, the occupier shown alongside the owner), and every resource the province currently owns — mirroring the existing selected-country panel and closing the province-selection UI `// TODO` left by `Docs/Specs/26_07_11_09_province-map-lens/spec.md`.

**Key acceptance criteria (design targets):**
- Panel opens on province click in the Province lens; hides when selection is cleared or the lens switches away from Province (selection itself is preserved, only the panel visibility changes).
- Header shows the localized province name, never a raw id.
- Owner row shows flag + localized country name; resources section lists every resource the province currently owns, unfiltered, with no placeholder when empty.
- Unoccupied (or occupier == owner): only the owner row shown, full color. Occupied by a different country: owner row grayed out/semi-transparent, occupier row full-color, both side by side in one row.
- Clicking the owner or occupant row selects that country and switches the map lens off Province so the country panel becomes visible.
- Owner/occupier changes while the panel is visible (e.g. via existing debug cheats) refresh the rows immediately, no stale data.

**Out of scope:**
- Any curation/filtering/capping of displayed province resources (tracked separately by GitHub issue #41).
- New province-scoped resource types, collectors, or `ResourceEffect`s.
- Province-level control, characters, or actions display (no equivalents of `CountryInfoView`'s control row / characters slide / actions slide).
- Any change to province occupation semantics, the existing debug occupation commands, or `ProvinceOccupationSystem`.
- Any change to `MapClickHandler`'s existing per-lens click routing beyond the new in-panel click-to-select rows.
- Any change to the province geometry/config generation pipeline.
- Hover-triggered/tooltip-only previews (this is click-to-select, matching the country panel).
- Multi-province selection/comparison, or any change to `SelectedProvinceState` supporting more than one concurrently selected province.
- Any change to the existing debug-only "Selected province" menu in `HUDDocument.cs` (`RefreshSelectedProvinceDebugMenu` and its cheat buttons).

## Goal

Add a new `province-info` UI Toolkit panel (view class + UXML/USS) driven entirely by existing `VisualState`/command plumbing, reusing `ResourcesView`'s generic resource rendering and the `CountryInfoView` owner-chip pattern. Fix a latent bug in `VisualStateConverter.UpdateSelectedProvince` (province deselection currently leaves `SelectedProvince.IsValid == true` with an empty id) that would otherwise stop the panel from ever hiding on deselect. No changes to ECS ownership/occupation semantics, map click routing, or the debug menu.

## Approach

### 1. Fix `SelectedProvince.IsValid` on deselect (`src/Game.Main/VisualStateConverter.cs`)

`ApplySelectProvince("")` (pushed by `MapClickHandler.HandleProvinceClick` on an open-water click) mutates the existing `ProvinceSelection` entity's `ProvinceId` to `""` rather than removing the component. `UpdateSelectedProvince` currently only checks `arch.Count == 0`, so once a `ProvinceSelection` entity has ever been created, `_state.SelectedProvince.Set(true, "")` fires on every later open-water click — `IsValid` stays `true` with an empty id. This is a pre-existing defect uncovered while grounding this plan (the spec's Tech Notes assume clearing already sets `IsValid = false`); it must be fixed here because the panel's visibility (and the "hides when selection is cleared" acceptance criterion) depends on it.

Fix, and extend in the same pass to populate the new province resources sub-state (see step 2):

```csharp
public void UpdateSelectedProvince(IReadOnlyWorld world) {
	int[] required = { TypeId<ProvinceSelection>.Value };
	foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
		if (arch.Count == 0) {
			continue;
		}
		ProvinceSelection[] selections = arch.GetColumn<ProvinceSelection>();
		string provinceId = selections[0].ProvinceId;
		if (!string.IsNullOrEmpty(provinceId)) {
			_state.SelectedProvince.Set(true, provinceId);
			_state.SelectedProvince.Resources.Set(true, provinceId, BuildResources(world, provinceId));
			return;
		}
	}
	_state.SelectedProvince.Set(false, "");
	_state.SelectedProvince.Resources.Set(false, "", new List<ResourceStateEntry>());
}
```

Make this method `public` (it is currently `void UpdateSelectedProvince`, package-private-by-default) following the existing `UpdateLeaderboards` precedent (`.claude/rules/csharp/code_style.md`'s "prefer public over InternalsVisibleTo" rule) so `src/Game.Tests/` can call it directly without routing through the full `Update(...)` pipeline or `InternalsVisibleTo`.

`BuildResources(world, provinceId)` (existing, line ~373) already works unmodified for any owner-id string, including a `provinceId` — it queries `ResourceOwner`/`Resource` entities by `OwnerId`, and today only `population` (`ResourceOwner.OwnerType.Province`) is keyed this way. No changes needed to `BuildResources`.

Do not reorder the existing `Update(...)` call sequence (`UpdateProvinceOwnership` → `UpdateProvinceOccupation` → `UpdateSelectedProvince` → ...) — the fix and the resources population both live inside the existing `UpdateSelectedProvince` call, not in `UpdateResources` (which runs earlier in `Update`, before `UpdateSelectedProvince` this same tick, and would read a stale `SelectedProvince.ProvinceId` if used instead).

### 2. `SelectedProvince.Resources` sub-state (`src/Game.Main/VisualState.cs`)

Add a `Resources` field to `SelectedProvinceState`, reusing the existing `CountryResourcesState` type exactly the way `SelectedCountryState.Resources` and `PlayerOrganizationState.Resources` already do — **not** a bespoke lighter-weight list type. This is a deliberate deviation from a plain-list-field option considered during grounding: reusing `CountryResourcesState` means `ResourcesView.Refresh(CountryResourcesState)` (`Assets/Scripts/Unity/UI/ResourcesView.cs`) needs **zero changes** to render province resources — the `ControlIncomes`/`CountryId` fields on `CountryResourcesState` simply stay empty/unused for a province (provinces never have a `gold` resource per Out of Scope, so the gold-control-income tooltip branch in `ResourcesView.BuildResourceTooltip` is dead code for this call site, not a correctness issue).

```csharp
public class SelectedProvinceState : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;

	public bool IsValid { get; private set; }
	public string ProvinceId { get; private set; } = "";
	public CountryResourcesState Resources { get; } = new CountryResourcesState();

	public void Set(bool isValid, string provinceId) {
		IsValid = isValid;
		ProvinceId = provinceId;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
	}
}
```

`CountryResourcesState` is already `public` and already raises its own `PropertyChanged` independently of the owning `SelectedProvinceState.Set` call — this is the same two-events-per-selection shape `SelectedCountryState`/`CountryResourcesState` already have, so HUD-side subscription follows the exact existing pattern (see step 5).

### 3. New view class `ProvinceInfoView` (`Assets/Scripts/Unity/UI/ProvinceInfoView.cs`)

Plain C# view class (not a MonoBehaviour), following the `CountryInfoView`/`ResourcesView` split (`.claude/rules/unity/uitoolkit.md`):

```csharp
#nullable enable
using System;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class ProvinceInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly VisualElement? _ownerRow;
		readonly VisualElement? _ownerFlag;
		readonly Label? _ownerName;
		readonly VisualElement? _occupantRow;
		readonly VisualElement? _occupantFlag;
		readonly Label? _occupantName;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CountryVisualConfig? _countryVisualConfig;
		string _ownerId = "";
		string _occupantId = "";

		public event Action<string>? OnCountryRowClicked;

		public ProvinceInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip, CountryVisualConfig? countryVisualConfig) {
			_root = root;
			_name = root.Q<Label>("province-name");
			_ownerRow = root.Q("province-owner-row");
			_ownerFlag = root.Q("province-owner-flag");
			_ownerName = root.Q<Label>("province-owner-name");
			_occupantRow = root.Q("province-occupant-row");
			_occupantFlag = root.Q("province-occupant-flag");
			_occupantName = root.Q<Label>("province-occupant-name");
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_resourcesView = new ResourcesView(root.Q("province-resources-container"), loc, resourceConfig, tooltip);

			if (_ownerRow != null) {
				_ownerRow.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _ownerRow.ContainsPoint(e.localPosition) && !string.IsNullOrEmpty(_ownerId)) {
						OnCountryRowClicked?.Invoke(_ownerId);
					}
				});
			}
			if (_occupantRow != null) {
				_occupantRow.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _occupantRow.ContainsPoint(e.localPosition) && !string.IsNullOrEmpty(_occupantId)) {
						OnCountryRowClicked?.Invoke(_occupantId);
					}
				});
			}
		}

		public void Refresh(bool visible, string provinceId, string ownerId, string occupierId, CountryResourcesState resources) {
			_root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			if (!visible) {
				return;
			}

			_name.text = _loc.Get($"province_name.{provinceId}");

			_ownerId = ownerId;
			bool isOccupied = !string.IsNullOrEmpty(occupierId) && occupierId != ownerId;
			_occupantId = isOccupied ? occupierId : "";

			SetCountryChip(_ownerFlag, _ownerName, ownerId);
			_ownerRow?.EnableInClassList("province-owner-row--occupied", isOccupied);

			if (_occupantRow != null) {
				_occupantRow.style.display = isOccupied ? DisplayStyle.Flex : DisplayStyle.None;
				if (isOccupied) {
					SetCountryChip(_occupantFlag, _occupantName, occupierId);
				}
			}

			_resourcesView.Refresh(resources);
		}

		void SetCountryChip(VisualElement? flagEl, Label? nameLabel, string countryId) {
			if (nameLabel != null) {
				nameLabel.text = string.IsNullOrEmpty(countryId) ? "" : _loc.Get($"country_name.{countryId}");
			}
			if (flagEl == null) {
				return;
			}
			var sprite = _countryVisualConfig?.Find(countryId)?.flag;
			if (sprite != null) {
				flagEl.style.backgroundImage = new StyleBackground(sprite);
				flagEl.style.display = DisplayStyle.Flex;
			} else {
				flagEl.style.display = DisplayStyle.None;
			}
		}
	}
}
```

Notes:
- `PointerUpEvent` + manual `ContainsPoint` — never `Button.clicked`/`ClickEvent` — per the documented Unity 6000.4.1f1 bug (`.claude/rules/unity/uitoolkit.md`), mirroring `CountryInfoView`'s `_charsToggleBtn` pattern exactly.
- The occupant row is independently clickable regardless of the owner row's grayed-out state — both rows register their own `PointerUpEvent` callback, neither is disabled.
- `_root.style.display` follows `CountryInfoView.Refresh`'s existing `DisplayStyle.None`/`Flex` pattern for the whole-panel show/hide.

### 4. New UXML/USS (`Assets/UI/HUD/ProvinceInfo/ProvinceInfo.uxml`, `ProvinceInfo.uss`)

`ProvinceInfo.uxml` (its own document, importing `SharedStyles.uss` directly — same as `CountryInfo.uxml` — so classes created in C# and added to containers inside this template resolve correctly per the "USS scope" rule in `.claude/rules/unity/uitoolkit.md`):

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
    <ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>
    <ui:Style src="project://database/Assets/UI/HUD/ProvinceInfo/ProvinceInfo.uss" />
    <ui:VisualElement name="province-bar" class="province-bar">
        <ui:Label name="province-name" class="province-name-header" text="" />
        <ui:VisualElement name="province-owner-occupant-row" class="province-owner-occupant-row">
            <ui:VisualElement name="province-owner-row" class="flag-name-row province-country-chip">
                <ui:VisualElement name="province-owner-flag" class="entity-flag" picking-mode="Ignore" />
                <ui:Label name="province-owner-name" class="province-country-name" text="" />
            </ui:VisualElement>
            <ui:VisualElement name="province-occupant-row" class="flag-name-row province-country-chip" style="display: none;">
                <ui:VisualElement name="province-occupant-flag" class="entity-flag" picking-mode="Ignore" />
                <ui:Label name="province-occupant-name" class="province-country-name" text="" />
            </ui:VisualElement>
        </ui:VisualElement>
        <ui:VisualElement name="province-resources-container" class="resources-container" />
    </ui:VisualElement>
</ui:UXML>
```

`flag-name-row` and `entity-flag` are shared classes already defined in `SharedStyles.uss` (lines ~653/658) — reused verbatim, not redefined, per the class-catalogue rule.

`ProvinceInfo.uss` (new classes scoped to this document only, deliberately not reusing `CountryInfo.uss`'s private `.country-name`/`.country-bar` classes, which live in a different document/template):

```css
.province-bar {
    flex-direction: column;
}

.province-name-header {
    font-size: 28px;
    color: rgb(240, 232, 208);
    -unity-font-style: bold;
    margin-bottom: 8px;
}

.province-country-name {
    font-size: 22px;
    color: rgb(240, 232, 208);
}

.province-owner-occupant-row {
    flex-direction: row;
    align-items: center;
    margin-bottom: 8px;
}

.province-country-chip {
    margin-right: 24px;
}

.province-owner-row--occupied {
    opacity: 0.5;
}
```

`.province-owner-row--occupied` follows the existing disabled/grayed-out precedent (`.character-card--empty { opacity: 0.5; }` in `SharedStyles.uss`, line ~371) — a dedicated class toggled via `EnableInClassList`, not an inline style.

### 5. Wire into `HUD.uxml` / `HUD.uss`

`Assets/UI/HUD/HUD.uxml` — add a template declaration alongside the existing ones and an instance next to `country-info` (order doesn't matter since the two panels are mutually exclusive by lens, but placing it right after `country-info` keeps related panels together):

```xml
<ui:Template name="ProvinceInfo" src="project://database/Assets/UI/HUD/ProvinceInfo/ProvinceInfo.uxml"/>
...
<ui:Instance template="ProvinceInfo" name="province-info" class="province-info-panel"/>
```

`Assets/UI/HUD/HUD.uss` — add a new selector, duplicating the existing bottom-bar rule rather than sharing `.country-info-panel`, matching this codebase's own precedent (`.org-lens-country-info-panel` already duplicates the identical block instead of reusing the class):

```css
.province-info-panel {
    position: absolute;
    bottom: 0;
    left: 0;
    right: 0;
    background-color: rgb(26, 42, 72);
    border-color: rgb(200, 160, 64);
    border-top-width: 3px;
    border-left-width: 0;
    border-right-width: 0;
    border-bottom-width: 0;
    border-radius: 0;
    padding: 16px 16px;
    display: none;
}
```

`display: none` as the default (matching `.org-lens-country-info-panel`) since `ProvinceInfoView.Refresh` controls visibility explicitly from the first `OnEnable`/`Start` refresh onward.

### 6. `HUDDocument` integration (`Assets/Scripts/Unity/UI/HUDDocument.cs`)

**Fields:** add `ProvinceInfoView _provinceInfo;` and `VisualElement _provinceInfoRoot;` alongside the existing `_countryInfo`/`_countryInfoRoot` fields.

**`Start()`:** construct right after `_countryInfo` is built:

```csharp
_provinceInfoRoot = _root.Q("province-info");
_provinceInfo = new ProvinceInfoView(_provinceInfoRoot, _loc, _resourceConfig, _tooltip, _countryVisualConfig);
_provinceInfo.OnCountryRowClicked += HandleProvinceInfoCountryRowClicked;
```

**New refresh method**, kept parallel to (not folded into) `RefreshCountryViews()` since the two panels are driven by different subsets of `PropertyChanged` events:

```csharp
void RefreshProvinceInfoView() {
	if (_provinceInfo == null || _state == null) {
		return;
	}
	bool visible = _state.MapLens.Lens == MapLens.Province && _state.SelectedProvince.IsValid;
	string provinceId = _state.SelectedProvince.ProvinceId;
	string ownerId = GetProvinceOwner(provinceId);
	string occupierId = GetProvinceOccupier(provinceId);
	_provinceInfo.Refresh(visible, provinceId, ownerId, occupierId, _state.SelectedProvince.Resources);
}
```

Reuses the existing private `GetProvinceOwner`/`GetProvinceOccupier` helpers (already on `HUDDocument`, currently only used by the debug menu — see Grounding) unchanged.

**Click-to-select handler:**

```csharp
void HandleProvinceInfoCountryRowClicked(string countryId) {
	if (string.IsNullOrEmpty(countryId)) {
		return;
	}
	_commands.Push(new SelectCountryCommand(countryId));
	_commands.Push(new ChangeLensCommand { Lens = MapLens.Political });
}
```

Both commands are pushed in the same frame/handler call, satisfying the same-tick command-ordering convention (`.claude/rules/unity/game_loop_integration.md`, generalized here from "action before pause" to "both land in the same tick" — there is no ordering dependency between `SelectCountryCommand` and `ChangeLensCommand` themselves, `SelectCountrySystem.Update` and the `ChangeLensCommand` loop in `GameLogic.Update` are independent, so push order between the two does not matter, only that neither is deferred to a later frame).

**Subscriptions — `OnEnable`/`OnDisable`:** add one new subscription pair (the other three events needed — `SelectedProvince`, `ProvinceOwnership`, `ProvinceOccupation`, `MapLens` — are already subscribed by the existing debug-menu code):

```csharp
// OnEnable:
_state.SelectedProvince.Resources.PropertyChanged += HandleSelectedProvinceResourcesChanged;
...
RefreshProvinceInfoView(); // alongside the existing RefreshCountryViews() call

// OnDisable:
_state.SelectedProvince.Resources.PropertyChanged -= HandleSelectedProvinceResourcesChanged;
if (_provinceInfo != null) { _provinceInfo.OnCountryRowClicked -= HandleProvinceInfoCountryRowClicked; }
```

**Extend existing handlers** to also call `RefreshProvinceInfoView()` (none of these currently refresh anything but the debug menu / dropdown state for province-related events):

```csharp
void HandleLensChanged(object sender, PropertyChangedEventArgs e) {
	_lensSwitcher?.Refresh(_state.MapLens.Lens);
	RefreshCountryViews();
	RefreshProvinceInfoView();          // add
	RefreshSelectedProvinceDebugMenu();
}

void HandleSelectedProvinceChanged(object sender, PropertyChangedEventArgs e) {
	RefreshProvinceInfoView();          // add
	RefreshSelectedProvinceDebugMenu();
}

void HandleProvinceOwnershipChanged(object sender, PropertyChangedEventArgs e) {
	_lastProvinceIdForDropdown = "";
	RefreshProvinceInfoView();          // add
	RefreshSelectedProvinceDebugMenu();
}

void HandleProvinceOccupationChanged(object sender, PropertyChangedEventArgs e) {
	RefreshProvinceInfoView();          // add
	RefreshProvinceActionButtons();
}

void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
	_loc.SetLocale(_state.Locale.Locale);
	RefreshLeaderboardButtonText();
	RefreshCountryViews();
	RefreshProvinceInfoView();          // add — province/country names are localized text
	_timeView.Refresh(_state.Time);
}

void HandleSelectedProvinceResourcesChanged(object sender, PropertyChangedEventArgs e) {
	RefreshProvinceInfoView();
}
```

No changes to `RefreshSelectedProvinceDebugMenu`, `RefreshProvinceActionButtons`, `BuildProvinceDebugUi`, or any `DebugChangeProvinceOwnerCommand`/`DebugSetProvinceOccupationCommand`/`DebugClearProvinceOccupationCommand` handling — per Out of Scope.

### 7. Assembly / DI

No new `.asmdef`, no new VContainer registration. `ProvinceInfoView` is plain C# constructed directly by `HUDDocument.Start()`, exactly like `CountryInfoView`/`PlayerOrgView` — it receives its dependencies (`ILocalization`, `ResourceConfig`, `TooltipSystem`, `CountryVisualConfig`) from fields `HUDDocument` already has injected via its existing `[Inject] Construct(...)` method. All Unity-side files stay in `Assets/Scripts/Unity/UI/` (existing asmdef); all `src/`-side changes stay in `Game.Main`.

## Steps

### Agent Steps

- [ ] **Fix `UpdateSelectedProvince` deselect bug and add resources population** — `src/Game.Main/VisualStateConverter.cs`: guard on non-empty `provinceId`, make the method `public`, call `BuildResources(world, provinceId)` into the new sub-state.
- [ ] **Add `SelectedProvinceState.Resources`** — `src/Game.Main/VisualState.cs`: add `CountryResourcesState Resources` field.
- [ ] **Write `src/Game.Tests/VisualStateConverterSelectedProvinceTests.cs`** — see Tests section.
- [ ] **Run `dotnet test src/GlobalStrategy.Core.sln`** — confirm new and existing tests pass.
- [ ] **Run `dotnet build src/GlobalStrategy.Core.sln -c Release`** — refresh `Assets/Plugins/Core/` DLLs with the `src/` changes.
- [ ] **Create `Assets/Scripts/Unity/UI/ProvinceInfoView.cs`** — new view class per Approach step 3.
- [ ] **Create `Assets/UI/HUD/ProvinceInfo/ProvinceInfo.uxml` and `ProvinceInfo.uss`** — per Approach step 4.
- [ ] **Edit `Assets/UI/HUD/HUD.uxml`** — add the `ProvinceInfo` template declaration and `province-info` instance.
- [ ] **Edit `Assets/UI/HUD/HUD.uss`** — add `.province-info-panel`.
- [ ] **Edit `Assets/Scripts/Unity/UI/HUDDocument.cs`** — fields, `Start()` construction, `RefreshProvinceInfoView()`, click-to-select handler, subscription wiring, and the five extended handlers, per Approach step 6.
- [ ] **Refresh Unity and check console** — `refresh_unity` then `read_console(types=["error"])`; fix any compile errors before proceeding.
- [ ] **Confirm no stray references** — grep `HUDDocument.cs` for the exact five handler names touched to double check every one now calls `RefreshProvinceInfoView()`, and confirm `RefreshSelectedProvinceDebugMenu`/`RefreshProvinceActionButtons`/`BuildProvinceDebugUi`/debug commands were left untouched.

### User Steps

### 1. Confirm UXML/USS import and panel layout in the Editor

After Unity finishes importing the new `ProvinceInfo.uxml`/`ProvinceInfo.uss` files and HUD scene reload, open the HUD scene (or enter Play mode) and confirm the console shows no UI Toolkit import/binding errors, and that the new `province-info` panel instance appears in the UI Builder/hierarchy under `hud-root` with the expected child structure (name label, owner row, occupant row, resources container).

### 2. Verify panel visibility and content in Play mode

Enter Play mode, switch to the Province lens, click a province. Confirm the province info panel opens showing the correct localized province name, owner flag + name, and the province's resources (population). Click open water and confirm the panel hides. Re-select a province, then switch to a non-Province lens and confirm the panel hides while the country panel (or org-lens panel) becomes visible as appropriate — and confirm switching back to the Province lens re-shows the panel for the still-selected province without needing to click it again.

### 3. Verify occupation display and click-to-select

Using the existing "Selected province" debug menu cheats (`Change owner`, `Change occupation`, `Reset occupation`), set a province's occupier to a country different from its owner. Confirm both owner (grayed out) and occupier (full color) rows appear side by side, and that both are independently clickable — clicking either row selects that country and switches the lens off Province, showing that country's own info panel. Clear the occupation and confirm the occupant row disappears and only the (now full-color) owner row remains, and that the panel refreshes immediately (no stale flag/name) without needing to reselect the province.

### 4. Verify empty-resources and locale behavior

Confirm the resources section shows no placeholder text if a province ever has zero resources (not reachable today since every province seeds `population`, but confirm the container is simply empty rather than showing an error/blank row if you can force this via a debug cheat). Switch the game's locale (Settings) while the panel is visible and confirm the province name and owner/occupant country names update to the new locale without needing to reselect.

## Tests

Test project: `src/Game.Tests/` (xUnit, existing project conventions).

- **New `src/Game.Tests/VisualStateConverterSelectedProvinceTests.cs`:**
  - `seed_helpers` — small `World` builder helpers analogous to `VisualStateConverterLeaderboardTests.SeedCountry`: create a `ProvinceSelection` entity (`world.Create()` + `world.Add(e, new ProvinceSelection { ProvinceId = ... })`), and a resource entity (`world.Add(e2, new ResourceOwner(provinceId, OwnerType.Province))` + `world.Add(e2, new Resource { ResourceId = "population", Value = ... })`).
  - `selecting_province_populates_its_resources` — seed a `ProvinceSelection` with a non-empty id plus a matching `population` resource entity, call `converter.UpdateSelectedProvince(world)`, assert `state.SelectedProvince.IsValid == true`, `state.SelectedProvince.ProvinceId` matches, and `state.SelectedProvince.Resources.Resources` contains the `population` entry with the expected value.
  - `deselecting_with_empty_province_id_clears_is_valid_and_resources` — seed a `ProvinceSelection` entity with `ProvinceId = ""` (reproducing the exact `ApplySelectProvince("")` shape used for open-water clicks), call `UpdateSelectedProvince`, assert `IsValid == false`, `ProvinceId == ""`, and `Resources.Resources` is empty. This is the regression test for the deselect bug fixed in Approach step 1 — without the `string.IsNullOrEmpty` guard, this test fails with `IsValid == true`.
  - `resources_are_scoped_to_the_selected_province_only` — seed two provinces' resource entities, select one, assert only that province's resources appear (proves `BuildResources(world, provinceId)` filtering, reused unmodified from the country path, works identically for provinces).
  - `no_province_selection_entity_at_all_leaves_state_invalid` — call `UpdateSelectedProvince` on a `World` with no `ProvinceSelection` component ever created (the very-first-tick case, before any click), assert `IsValid == false` and resources empty (covers the pre-existing `arch.Count == 0`/no-matching-archetype fallthrough path, unchanged by this plan but worth locking down alongside the new tests).

- Run `dotnet test src/GlobalStrategy.Core.sln` and `dotnet build src/GlobalStrategy.Core.sln -c Release` (Agent Steps already cover this; re-run after any later edit to `VisualState.cs`/`VisualStateConverter.cs`).

- **Manual/Unity checks** (see User Steps 1–4 above): panel show/hide on select/deselect/lens-switch, header/owner/resources content, occupied side-by-side display and independent click-to-select on both rows, immediate refresh via the existing debug ownership/occupation cheats, and locale-switch refresh.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *Unity 6 + URP only.* No rendering changes at all — this is a UI Toolkit panel plus `src/` state plumbing; no shaders, materials, or camera work.
- *ECS for all game logic in `src/`.* The only new/changed logic (deselect-bug fix, resources population) lives in `src/Game.Main/VisualStateConverter.cs`, reading existing ECS `ResourceOwner`/`Resource`/`ProvinceSelection` components. `HUDDocument`/`ProvinceInfoView` are presentation/input glue only — they read `VisualState` and push existing commands (`SelectCountryCommand`, `ChangeLensCommand`), never touching `World` directly.
- *VContainer sole DI.* No new registrations; `ProvinceInfoView` is constructed directly by the already-injected `HUDDocument`, exactly like `CountryInfoView`.
- *UI Toolkit only.* New panel is UXML/USS + a MonoBehaviour-owned plain view class; no Canvas/UGUI.
- *Planning before implement.* This plan is written and awaiting approval before any code/asset change.
- *Spec before plan for feature work.* Follows the approved `Docs/Specs/26_07_22_08_province-info-panel/spec.md`.
- *File organisation.* Lives at `Docs/Specs/26_07_22_08_province-info-panel/plan.md`, not legacy `Docs/Plans/`.
- *Assembly structure.* New Unity script (`ProvinceInfoView.cs`) stays in the existing `Assets/Scripts/Unity/UI/` folder/asmdef; no new feature folder or cross-folder assembly.
- *C# style.* Tabs, `_`-prefixed private fields, braces always, no redundant access modifiers — followed throughout the code shown above, matching `CountryInfoView`/`ResourcesView`'s existing style.

Use the implement skill to start working on the plan or request changes.
