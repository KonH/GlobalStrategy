# Plan: Org and Country Flags — Stage 2: Unity Implementation

## Spec

Stage 2 wires the flag/org PNG assets downloaded in Stage 1 into the Unity UI layer. This means adding a `Sprite flag` field to `CountryVisualEntry` and `OrgVisualEntry`, defining shared USS classes for the flag image element, updating six UXML files to wrap each entity-name label in a flex row containing a flag image element, updating the corresponding C# view classes and MonoBehaviours to query the flag element and set its background from the visual config, extending constructors and `[Inject]` methods to accept the relevant config where it is currently missing, and providing user-facing instructions for assigning the 21 Sprite assets in the Unity Inspector.

## Goal

Display a 64×64 px flag image to the left of every country and org name label throughout the UI, with graceful fallback (no image shown, no error) when the Sprite field is unassigned.

## Approach

All flag display logic is purely in the Unity UI binding layer — no changes to ECS, game logic, or save data. Each display location follows the same pattern: a `VisualElement` row container wrapping a flag `VisualElement` and the existing name `Label`. The flag element's `backgroundImage` style property is set from the `flag` Sprite on the matching config entry, and the element is hidden when the Sprite is null. Shared USS classes keep the visual definition in one place. Where a view class or MonoBehaviour does not yet receive the relevant visual config, it is added to the constructor signature or `[Inject]` method and passed through from the calling site.

## Agent Steps

- [x] **Step 1 — Add `flag` Sprite field to `CountryVisualEntry`** — In `Assets/Scripts/Unity/Map/Config/CountryVisualEntry.cs`, add `public Sprite flag;` after the `color` field, following the same pattern as `CharacterVisualEntry.portrait`.

- [x] **Step 2 — Add `flag` Sprite field to `OrgVisualEntry`** — In `Assets/Scripts/Unity/Map/Config/OrgVisualEntry.cs`, add `public Sprite flag;` after the `color` field.

- [x] **Step 3 — Add shared USS classes to `SharedStyles.uss`** — In `Assets/UI/Shared/SharedStyles.uss`, append two new rules:
  - `.flag-name-row` — `flex-direction: row; align-items: center; gap: 8px;`
  - `.entity-flag` — `width: 64px; height: 64px; flex-shrink: 0; -unity-background-scale-mode: scale-to-fit;`

- [x] **Step 4 — Update `CountryInfo.uxml`** — In `Assets/UI/HUD/CountryInfo/CountryInfo.uxml`, replace the bare `<ui:Label name="country-name" class="country-name" text="" />` with a wrapper `<ui:VisualElement name="country-name-row" class="flag-name-row">` containing a `<ui:VisualElement name="country-flag" class="entity-flag" />` and the original label (same name and class attributes) as siblings inside it.

- [x] **Step 5 — Update `OrgLensCountryInfo.uxml`** — In `Assets/UI/HUD/OrgLensCountryInfo/OrgLensCountryInfo.uxml`, replace `<ui:Label name="org-name" class="gs-label" text="" />` with a `<ui:VisualElement name="org-name-row" class="flag-name-row">` wrapping a `<ui:VisualElement name="org-flag" class="entity-flag" />` and the original label.

- [x] **Step 6 — Update `OrgInfo.uxml`** — In `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml`, replace `<ui:Label name="org-name" class="org-name" text="" />` (inside `org-main-block`) with a `<ui:VisualElement name="org-name-row" class="flag-name-row">` wrapping a `<ui:VisualElement name="org-flag" class="entity-flag" />` and the original label.

- [x] **Step 7 — Update `PlayerCountry.uxml`** — In `Assets/UI/HUD/PlayerCountry/PlayerCountry.uxml`, replace `<ui:Label name="player-country-name" class="gs-header player-country-name" text="" />` with a `<ui:VisualElement name="player-org-name-row" class="flag-name-row">` wrapping a `<ui:VisualElement name="player-org-flag" class="entity-flag" />` and the original label (same name and class attributes).

- [x] **Step 8 — Update `SelectCountry.uxml`** — In `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`, replace `<ui:Label name="country-name-label" text="" class="gs-header country-name"/>` with a `<ui:VisualElement name="org-name-row" class="flag-name-row">` wrapping a `<ui:VisualElement name="org-flag" class="entity-flag" />` and the original label (same name and class). Note: this UXML is used by `SelectOrgDocument`, which displays org name/data in the SelectCountry scene — the flag shown here is the org flag.

- [x] **Step 9 — Verify `HUDDocument` already has `CountryVisualConfig` injection** — Read `Assets/Scripts/Unity/UI/HUDDocument.cs` fully to confirm whether `CountryVisualConfig _countryVisualConfig` is already a field and injected. The `[Inject]` method currently accepts `CharacterVisualConfig` but likely not `CountryVisualConfig`. If absent, add `CountryVisualConfig countryVisualConfig` to the `[Inject]` parameter list and store it. `CountryVisualConfig` is already registered in `GameLifetimeScope` — no new registration needed.

- [x] **Step 10 — Update `CountryInfoView.cs`** — In `Assets/Scripts/Unity/UI/CountryInfoView.cs`:
  - Add `CountryVisualConfig` and `OrgVisualConfig` parameters to the constructor signature (after `actionVisualConfig`).
  - Store both configs as readonly fields `_countryVisualConfig` and `_orgVisualConfig`.
  - In the constructor body, query `_flagElement = root.Q("country-flag")` and store as `readonly VisualElement? _flagElement`.
  - In `Refresh()`, after setting `_name.text`, look up `_countryVisualConfig?.Find(selected.CountryId)`, get its `flag` Sprite, set `_flagElement.style.backgroundImage = new StyleBackground(sprite)` when sprite is non-null and hide the element (`DisplayStyle.None`) when null — only when `selected.IsValid`.
  - In `BuildControlTooltip()`, replace the plain `new Label(...)` row for each org entry with a container `VisualElement` with class `flag-name-row` holding a flag `VisualElement` (class `entity-flag`) and a `Label`. Look up the org entry via `_orgVisualConfig?.Find(entry.OrgId)` to get the Sprite; set `backgroundImage` and hide the flag element if null. Apply `PickingMode.Ignore` to the flag element (it is non-interactive).

- [x] **Step 11 — Update `HUDDocument.cs` to pass configs to `CountryInfoView`** — In `Assets/Scripts/Unity/UI/HUDDocument.cs`:
  - Add `OrgVisualConfig _orgVisualConfig;` field.
  - In the `[Inject] void Construct(...)` method, add `OrgVisualConfig orgVisualConfig` parameter and assign `_orgVisualConfig = orgVisualConfig`.
  - In `Start()`, update the `CountryInfoView` constructor call to pass `_countryVisualConfig` (confirmed present or added in Step 9) and `_orgVisualConfig` as the two new trailing arguments.
  - Note: `CountryVisualConfig` and `OrgVisualConfig` are already registered singletons in `GameLifetimeScope` — no new registrations needed.

- [x] **Step 12 — Update `OrgLensCountryView.cs`** — In `Assets/Scripts/Unity/UI/OrgLensCountryView.cs`:
  - Add `OrgVisualConfig` parameter to the constructor (alongside `root`). Store as `readonly OrgVisualConfig? _orgVisualConfig`.
  - Query `_flagElement = root.Q("org-flag")` in the constructor; store as `readonly VisualElement? _flagElement`.
  - In `Refresh()`, in the branch where `found != null`, after setting `_orgName.text`, look up `_orgVisualConfig?.Find(found.TopOrgId)`, get its `flag` Sprite, set `backgroundImage` on `_flagElement`, and show/hide `_flagElement` based on null.
  - In the `else` branch and in `Hide()`, hide `_flagElement`.

- [x] **Step 13 — Update `HUDDocument.cs` to pass `OrgVisualConfig` to `OrgLensCountryView`** — In `Assets/Scripts/Unity/UI/HUDDocument.cs`, update the `new OrgLensCountryView(...)` constructor call in `Awake()` to pass `_orgVisualConfig` (already added to the `[Inject]` method in Step 11).

- [x] **Step 14 — Update `OrgInfoDocument.cs`** — In `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`:
  - Add `OrgVisualConfig _orgVisualConfig;` field.
  - In `[Inject] void Construct(...)`, add `OrgVisualConfig orgVisualConfig` parameter and assign `_orgVisualConfig = orgVisualConfig`.
  - In `Awake()`, query `_orgFlagElement = docRoot.Q("org-flag")` after the other element queries; store as `VisualElement _orgFlagElement`.
  - In `Refresh()`, after setting `_orgName.text = org.DisplayName`, add a null-guarded block: `if (_orgFlagElement != null) { var sprite = _orgVisualConfig?.Find(org.OrgId)?.flag; _orgFlagElement.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : StyleBackground.None; _orgFlagElement.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None; }` — the null guard ensures graceful behaviour if the UXML step has not yet been applied.
  - `OrgVisualConfig` is already registered as a singleton in `GameLifetimeScope` — no new registration needed.

- [x] **Step 15 — Update `PlayerOrgView.cs`** — In `Assets/Scripts/Unity/UI/PlayerOrgView.cs`:
  - Add `OrgVisualConfig` parameter to the constructor (after `tooltip`). Store as `readonly OrgVisualConfig? _orgVisualConfig`.
  - Query `_flagElement = root.Q("player-org-flag")` in the constructor; store as `readonly VisualElement? _flagElement`.
  - In `Refresh()`, when `state.IsValid`, look up `_orgVisualConfig?.Find(state.OrgId)` — note: `PlayerOrganizationState` must expose `OrgId` (check `GS.Main.PlayerOrganizationState`; if it does, use it directly; if not, access it via the state that has it). Set `backgroundImage` on `_flagElement` and show/hide based on null.

- [x] **Step 16 — Update `HUDDocument.cs` to pass `OrgVisualConfig` to `PlayerOrgView`** — In `Assets/Scripts/Unity/UI/HUDDocument.cs`, update the `new PlayerOrgView(...)` constructor call in `Start()` to pass `_orgVisualConfig` as the new trailing argument (added in Step 11).

- [x] **Step 17 — Register `SelectOrgDocument` in `SelectCountryLifetimeScope` and update `SelectOrgDocument.cs`** — `SelectOrgDocument` is currently NOT registered in `SelectCountryLifetimeScope`, so its `[Inject]` method never fires. Two changes required:
  - In `Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs`, add `builder.RegisterComponentInHierarchy<SelectOrgDocument>();` to `Configure`. `OrgVisualConfig` is already registered via `builder.RegisterInstance(_orgVisualConfig)` — no additional registration needed.
  - In `Assets/Scripts/Unity/UI/SelectOrgDocument.cs`:
    - Add `OrgVisualConfig _orgVisualConfig;` field.
    - In `[Inject] void Construct(...)`, add `OrgVisualConfig orgVisualConfig` parameter and assign `_orgVisualConfig = orgVisualConfig`.
    - In `Start()`, after querying `_orgNameLabel`, query `_orgFlagElement = root.Q("org-flag")`; store as `VisualElement _orgFlagElement`.
    - In `RefreshUI()`, when `state.IsValid`, look up `_orgVisualConfig?.Find(state.OrgId)`, get `flag` Sprite, set `_orgFlagElement?.style.backgroundImage` and show/hide based on null. When not valid, hide the flag element.

- [x] **Step 18 — Verify `PlayerOrganizationState.OrgId` accessibility** — Search `GS.Main.PlayerOrganizationState` (in the `src/` C# solution or the compiled DLL's public API) to confirm `OrgId` is a public property. If the property name differs, adjust Step 15 accordingly. This is a read-only check — no code change needed if it already exists.

## User Steps

### 1. Import flag PNG assets as Sprites

Stage 1 downloads PNGs into `Assets/Textures/Flags/Countries/` and `Assets/Textures/Flags/Orgs/`. After Stage 1 runs, open the Unity Editor and let it import all new PNG files automatically. Then, for each PNG:
- Select all files in `Assets/Textures/Flags/Countries/` in the Project window.
- In the Inspector, set **Texture Type** to **Sprite (2D and UI)**.
- Click **Apply**.
- Repeat for all files in `Assets/Textures/Flags/Orgs/`.

### 2. Assign country flags in `CountryVisualConfig`

- In the Project window, navigate to `Assets/Configs/CountryVisualConfig.asset` and select it.
- In the Inspector, expand the **Entries** list. There is one entry per country.
- For each of the 20 available countries, find its entry by `countryId`, click the **Flag** Sprite field, and assign the corresponding Sprite from `Assets/Textures/Flags/Countries/<countryId>.png`.
- Leave the **Flag** field blank for any country that does not have a downloaded asset — the UI will show no image and no error.

### 3. Assign org flags in `OrgVisualConfig`

- In the Project window, navigate to `Assets/Configs/OrgVisualConfig.asset` and select it.
- In the Inspector, expand the **Entries** list.
- For the Illuminati entry (or whichever orgs have downloaded assets), click the **Flag** Sprite field and assign the corresponding Sprite from `Assets/Textures/Flags/Orgs/<orgId>.png`.
- Leave the **Flag** field blank for any org without an asset.

### 4. Verify in Play mode

- Enter Play mode and open the Map scene.
- Click a country with a flag assigned — confirm the 64×64 flag image appears to the left of the country name in the `CountryInfo` HUD panel.
- Click a country without a flag assigned — confirm the name label appears without any image or error.
- Open the Org Info panel — confirm the org flag appears beside the org name.
- Open the control tooltip on a country — confirm each org row in the tooltip shows the org flag beside the org name.
- Open the `CountrySelection` scene and select an org — confirm the org flag appears beside the org name in the selection panel.

## Constitution Check

- **Unity 6 + URP only** — no engine-version-specific APIs used; USS `gap` is used for row spacing (not `margin-left`), per the layout gotcha rules.
- **ECS for game logic** — no changes to any `src/` ECS code; all changes are in Unity UI binding layer only.
- **VContainer sole DI** — all new dependencies injected via `[Inject]` methods or constructor parameters; no `new` calls for shared config objects; `CountryVisualConfig` and `OrgVisualConfig` are already registered singletons in both `GameLifetimeScope` and `SelectCountryLifetimeScope`; `SelectOrgDocument` is added to `SelectCountryLifetimeScope` via `RegisterComponentInHierarchy` so VContainer can inject into it.
- **UI Toolkit only** — no Canvas/UGUI added; flag images use `VisualElement` with `backgroundImage` style.
- **One `.asmdef` per feature folder** — no new assemblies created; all changed files are in existing assemblies that already reference the necessary types (`GS.Unity.Map` for the config types, `GS.Unity.UI` for the views).
- **`PickingMode.Ignore`** applied to the flag `VisualElement` in dynamically created tooltip rows (non-interactive). In UXML, flag elements that are non-interactive should have `picking-mode="Ignore"` set in the UXML markup.
- **No `Button.clicked` or `ClickEvent`** — no new click handlers introduced in this plan.

Use /implement to start working on the plan or request changes.
