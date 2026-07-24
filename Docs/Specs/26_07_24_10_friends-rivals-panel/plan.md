# Plan: Friends & Rivals Panel in Selected Country View

## Spec

**Feature Intent:** As a player, I want to see the selected country's friends and rivals as flag rows in the country info panel, so that I can quickly understand its diplomatic relationships and jump to a related country by clicking its flag.

**Acceptance Criteria:**
- Selecting a country in Political-lens view shows a new block anchored to the right of the main country info and just before the Characters/Actions buttons: a "Friends" header + row of friend flags, and a "Rivals" header + row of rival flags.
- No friends → the "Friends" header and row are hidden entirely (no empty header). No rivals → same for "Rivals", independently. Neither → the whole block takes no visible space.
- Hovering a flag shows a tooltip with that country's localized display name.
- Clicking a flag selects that country, the panel refreshes to show its info/resources/relations, and any open Characters/Actions sub-panel closes (matching existing selection-change behavior).
- Changing the selected country via any path while the panel is visible updates the Friends/Rivals block with no stale flags left over.
- **Confirmed since spec approval:** no restrictions on flag clickability — every friend/rival flag is clickable and selects that country, with zero exceptions.

## Goal

Add a Friends/Rivals flag-row block to the Political-lens `CountryInfoView` panel, wired to the already-implemented `SelectedCountryState.Relations` data, with click-to-select and hover tooltips.

## Approach

This is mostly presentation-only work: no ECS/`VisualStateConverter` changes, since `CountryRelationsState.Friends`/`.Rivals` already exists and is populated by `UpdateCountryRelations`. One small `VisualState.cs` change is needed: `CountryRelationsState.Set` currently has no equality guard and is called unconditionally every tick, so before wiring a `PropertyChanged` subscription for it, add an order-independent equality check (mirroring `DiscoveredCountriesState.Set`'s `HashSet.SetEquals` pattern) so it only fires when Friends/Rivals actually change. Add a `relations-block` sibling to `CountryInfo.uxml` between `country-main-block` and `country-toggle-block` (both live inside the row-flex `country-bar`, so the new block naturally sits between them), containing a Friends sub-row and a Rivals sub-row, each with a header Label and a flags container. Extend `CountryInfoView` (no new view class) to query these elements, rebuild each flags container from `selected.Relations.Friends`/`.Rivals` on every `Refresh`, toggle each sub-row's `display` when its list is empty, and register a `PointerUpEvent` + tooltip trigger per flag. A new `OnRelatedCountryFlagClicked` event bubbles the clicked country ID up to `HUDDocument`, which pushes a plain `SelectCountryCommand` — the existing `_lastCountryId` change-detection block in `Refresh` already closes any open sub-panel on country switch, so no new logic is needed there. `HUDDocument` also subscribes to `_state.SelectedCountry.Relations.PropertyChanged` (alongside its existing `Control`/`Characters`/`CountryActions` subscriptions) so a relation added/removed via the debug menu while the same country stays selected refreshes the block immediately. Two new `hud.*` localization keys provide the header text.

## Steps

### Agent Steps

- [ ] **`VisualState.cs`: equality guard on `CountryRelationsState.Set`** — In `src/Game.Main/VisualState.cs`, change `CountryRelationsState.Set(IReadOnlyList<string> friends, IReadOnlyList<string> rivals)` to skip the `PropertyChanged` invocation when the new lists are set-equal (order-independent) to the current `Friends`/`Rivals`, mirroring `DiscoveredCountriesState.Set`'s `HashSet<string>.SetEquals` pattern (lines 232-240) rather than `IReadOnlyList<string>.SequenceEqual` (ECS iteration order for `CountryRelations.GetRelationsByCountryId` is not guaranteed stable between ticks even when membership is unchanged).

- [ ] **UXML: add `relations-block`** — In `Assets/UI/HUD/CountryInfo/CountryInfo.uxml`, insert a new `<ui:VisualElement name="relations-block" class="relations-block">` between `country-main-block` (ends line 25) and `country-toggle-block` (starts line 26), both children of `country-bar`. Inside it, add two column sub-blocks: `<ui:VisualElement name="friends-row-block" class="relations-row-block">` containing `<ui:Label name="friends-header" class="gs-label relations-header" text="Friends" />` and `<ui:VisualElement name="friends-flags" class="relations-flags" />`; and the equivalent `rivals-row-block` / `rivals-header` / `rivals-flags` triple for Rivals — reuse the shared `gs-label` class for typography, matching the existing `control-label` Label at line 22, rather than duplicating font size/colour in a new class.

- [ ] **USS: relations block + flag chip styles** — In `Assets/UI/HUD/CountryInfo/CountryInfo.uss`, add `.relations-block` (`flex-direction: column`), `.relations-row-block` (`flex-direction: column`), `.relations-header` (layout-only, e.g. spacing — no font-size/colour; typography comes from the shared `gs-label` class applied alongside it in UXML, per `.claude/rules/unity/uitoolkit.md`'s "per-feature USS is layout-only, never colour/font repetition" rule), `.relations-flags` (`flex-direction: row; gap: 4px;` — `gap` not `margin-left`, per the documented USS gotcha), and `.relations-flag` (28px × 28px square, `background-color`/border omitted unless needed, scaled between `.control-icon` at 22px and `.entity-flag` at 64px).

- [ ] **`CountryInfoView.cs`: query new elements** — In the constructor, query `relations-block` is not needed directly (sub-rows suffice); query `friends-row-block`, `friends-header`, `friends-flags`, `rivals-row-block`, `rivals-header`, `rivals-flags` via `root.Q(...)`/`root.Q<Label>(...)`, storing them as `readonly VisualElement?`/`readonly Label?` fields mirroring the `_flagElement`/`_controlRow` pattern (lines 14-17, 41-50). Set `friends-header.text = _loc.Get("hud.friends")` and `rivals-header.text = _loc.Get("hud.rivals")` in `Refresh`, right after the existing `_name.text = ...` line — confirmed via `HUDDocument.HandleLocaleChanged` (line 511) that `_loc.SetLocale(...)` is followed by `RefreshCountryViews()` → `_countryInfo.Refresh(...)` on every locale switch, so `Refresh` is the correct hook; there is no "full document reload" pattern in this codebase, so setting the text only in the constructor would leave stale-locale header text after a locale switch.

- [ ] **`CountryInfoView.cs`: add `OnRelatedCountryFlagClicked` event** — Add `public event Action<string>? OnRelatedCountryFlagClicked;` alongside the existing `OnCountryActionCardClicked` event declaration (line 35).

- [ ] **`CountryInfoView.cs`: build flag rows in `Refresh`** — After the existing `selected.IsValid` block (after line 98) and independent of `hasChars`/`hasActions`, add a `BuildRelationsRow(VisualElement? container, VisualElement? rowBlock, IReadOnlyList<string> countryIds)` helper: `container?.Clear()`; toggle `rowBlock.style.display` to `DisplayStyle.None` when `countryIds.Count == 0`, else `DisplayStyle.Flex`; for each country ID, create a `VisualElement` with class `relations-flag`, set `style.backgroundImage` from `_countryVisualConfig?.Find(countryId)?.flag` (same null-guard pattern as lines 90-96), leave `pickingMode` at its default `Position` (must NOT be `Ignore` — each flag is independently clickable), register a `PointerUpEvent` handler capturing the country ID that checks `e.button == 0 && flagEl.ContainsPoint(e.localPosition)` before invoking `OnRelatedCountryFlagClicked?.Invoke(countryId)`, register a tooltip via `_tooltip.RegisterTrigger(flagEl, $"relation-{countryId}-{index}", ctx => BuildRelationTooltip(countryId), new HashSet<string>())` (unique key per element using an index, in case a country could appear more than once), and `container.Add(flagEl)`. Call this helper twice from `Refresh`: once for `friends-flags`/`friends-row-block`/`selected.Relations.Friends`, once for `rivals-flags`/`rivals-row-block`/`selected.Relations.Rivals`.

- [ ] **`CountryInfoView.cs`: tooltip content builder** — Add `VisualElement BuildRelationTooltip(string countryId)` mirroring `BuildOrgControlInnerTooltip`'s simple-label shape (lines 241-274, minus the control/income rows): a single `Label(_loc.Get($"country_name.{countryId}"))` with class `tooltip-header` (reuses the existing tooltip document's stylesheet — no new USS class needed, per the USS-scope rule that tooltip content classes must live in the tooltip overlay's owning document, already satisfied since `tooltip-header` is already used from this same view).

- [ ] **`HUDDocument.cs`: subscribe/unsubscribe + handler** — In `Start()`, next to `_countryInfo.OnCountryActionCardClicked += HandleCountryActionCardClicked;` (line 120), add `_countryInfo.OnRelatedCountryFlagClicked += HandleRelatedCountryFlagClicked;`. In the teardown method, next to line 352, add `if (_countryInfo != null) { _countryInfo.OnRelatedCountryFlagClicked -= HandleRelatedCountryFlagClicked; }`. Add a new handler near `HandleProvinceInfoCountryRowClicked` (line 407): `void HandleRelatedCountryFlagClicked(string countryId) { if (string.IsNullOrEmpty(countryId)) { return; } _commands.Push(new SelectCountryCommand(countryId)); }` — no `ChangeLensCommand`, since `CountryInfoView` only renders in `RefreshCountryViews`'s Political-lens `else` branch (lines 386-392).

- [ ] **`HUDDocument.cs`: subscribe to relations changes** — In `OnEnable()`, next to `_state.SelectedCountry.CountryActions.PropertyChanged += HandleCountryActionsChanged;` (line 303), add `_state.SelectedCountry.Relations.PropertyChanged += HandleRelationsChanged;`; mirror the removal in `OnDisable()` next to line 337. Add `void HandleRelationsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();`, mirroring `HandleCountryActionsChanged` (line 532), so a relation added/removed via the debug menu while the same country stays selected refreshes the Friends/Rivals block immediately — safe against per-frame churn only because the previous step added the equality guard to `CountryRelationsState.Set`.

- [ ] **Localization: `en.asset`** — Add two entries to `Assets/Localization/en.asset` following the existing `- Key: ... \n  Value: ...` list shape (e.g. matching the `hud.actions` entry at lines 1643-1644): `hud.friends` = `Friends`, `hud.rivals` = `Rivals`.

- [ ] **Localization: `ru.asset`** — Add the matching two entries to `Assets/Localization/ru.asset` in the same list shape: `hud.friends` = `Друзья`, `hud.rivals` = `Соперники`.

- [ ] **Unity refresh + compile check** — After all `.cs`/`.uxml`/`.uss` edits, call `refresh_unity` then `read_console(types=["error"])` to confirm no compile errors (per `.claude/rules/unity/mcp_usage.md`).

### User Steps

#### 1. Visual verification in Play mode

Enter Play mode, switch to Political lens, and select a country with configured friends and/or rivals (the project seeds historical relations per `Docs/Specs/26_07_23_06_country-relations/`). Confirm: the Friends/Rivals block appears to the right of the main country info and just left of the Characters/Actions buttons; headers read "Friends"/"Rivals"; a country with no friends hides the Friends header+row entirely (no empty header), same independently for Rivals; a country with neither shows no block at all. Hover a flag and confirm the tooltip shows the correct country name. Click a flag and confirm the newly selected country's info panel refreshes (name, resources, and its own friends/rivals), including when a Characters or Actions sub-panel was open before the click (it should close). Change selection via a different path (map click) while the block is visible and confirm no stale flags from the previous country remain. With the same country still selected, use the "Selected country" debug menu's Set friend/Set rival/Clear relation actions (per `Docs/Specs/26_07_23_06_country-relations/`) and confirm the Friends/Rivals block updates immediately without needing to reselect.

#### 2. WebGL-safety spot check

Confirm the new UI introduces no emoji/Unicode symbol glyphs anywhere (headers and tooltips use plain ASCII text/localized strings only, flags are image assets) — no build-specific action expected, this is a quick visual confirmation that `.claude/rules/unity/webgl.md` wasn't inadvertently violated.

## Tests

- [ ] **`VisualStateChangeNotificationTests.cs`**: add `country_relations_state_no_op_set_does_not_fire_property_changed`, following the existing `discovered_countries_state_no_op_and_recently_discovered_ignored_in_equality_check` pattern (same file, ~line 97) — construct a `CountryRelationsState`, `Set` an initial `Friends`/`Rivals` pair, subscribe a fire counter, re-`Set` with the same membership in a different order (assert 0 fires), then `Set` with an actual membership change (assert 1 fire), covering both `Friends` and `Rivals` independently.

This is the only `Game.Tests` surface this feature touches — no `[Savable]` components, no `World` mutation, and no new command beyond pushing the already-tested `SelectCountryCommand`. The rest of the feature is Unity-side UI Toolkit rendering/event wiring (`CountryInfoView`, `HUDDocument`), which this project does not unit test. That part is verified manually, via the User Steps above: block position and header text, hide-when-empty behavior (Friends only, Rivals only, neither, both), tooltip content on hover, click-to-reselect (including sub-panel closing on reselect), stale-flag-free refresh on any selection-change path, live refresh when a relation changes via the debug menu while the same country stays selected, and WebGL-safe (no emoji/Unicode glyph) compliance.

## Constitution Check

No conflicts found — plan aligns with all principles.

- **Rendering:** No shader/material/camera changes; URP untouched.
- **Game Logic (ECS):** No ECS/`World`/system changes. The one `src/Game.Main/VisualState.cs` edit (equality guard on `CountryRelationsState.Set`) is presentation-state plumbing, not game logic — it mirrors the existing `DiscoveredCountriesState.Set` pattern in the same file and touches no ECS component or system. All other new code is Unity-side presentation/input glue (`CountryInfoView`, `HUDDocument`).
- **Dependency Injection:** No new singletons or `FindObjectOfType`; `CountryInfoView` continues to receive its dependencies (`_loc`, `_countryVisualConfig`, `_tooltip`) via its existing constructor, itself instantiated from the VContainer-injected `HUDDocument`.
- **UI:** UI Toolkit only — new elements are UXML/USS plus C#-built `VisualElement`s, no Canvas/UGUI.
- **Planning Discipline:** This plan is being produced before any code/asset change, per the gate.
- **Specification Discipline:** `spec.md` (feature work) precedes this plan, per `/specify`.
- **File Organisation:** Plan lives at `Docs/Specs/26_07_24_10_friends-rivals-panel/plan.md`, alongside `spec.md`.
- **Assembly Structure:** No new feature folder or assembly; edits stay within the existing `GS.Unity.UI` assembly (`CountryInfoView.cs`, `HUDDocument.cs`).
- **C# Code Style:** New code will use tabs, `_`-prefixed private fields, always-braces control flow, and no redundant access modifiers, consistent with the surrounding file.

Use the implement skill to start working on the plan or request changes.
