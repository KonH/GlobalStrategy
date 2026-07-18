# Plan: Org Lens Selection Panel

## Spec

### Feature Intent

As a player in Organization lens mode, I want the bottom selection panel to show the dominant organization in the country I click, so that I can quickly understand who controls each territory without switching lens or hunting through the control list.

### Acceptance Criteria

- Given the map is in MapLens.Political or MapLens.Geographic and the player clicks a country When the bottom panel renders Then it shows the existing CountryInfoView content — no change from current behaviour.

- Given the map is in MapLens.Org and the player clicks a country that has a TopOrgId entry in OrgMapState.Entries When the bottom panel renders Then the CountryInfoView is hidden and a new OrgLensCountryView is shown in its place, displaying:
  - The dominant org's display name

- Given the map is in MapLens.Org and the player clicks a country that has no entry in OrgMapState.Entries When the bottom panel renders Then the OrgLensCountryView shows the country with a "no dominant organization" indicator.

- Given the map is in MapLens.Org and no country is selected When the bottom panel renders Then OrgLensCountryView is hidden.

- Given the player is viewing OrgLensCountryView and switches the lens away from MapLens.Org When MapLensState.PropertyChanged fires Then the panel reverts to CountryInfoView.

- Given the player is in MapLens.Org with a country selected and OrgMapState updates When OrgMapState.PropertyChanged fires Then OrgLensCountryView refreshes.

### Out of Scope

- Clicking org entries to open OrgInfoDocument
- Showing org resources, gold, characters
- Changing Political/Geographic panel behaviour
- ECS system or VisualStateConverter changes
- Persisting state across scene loads

## Goal

Add a new `OrgLensCountryView` that replaces `CountryInfoView` in the bottom selection panel whenever the map is in `MapLens.Org`, displaying the dominant organization's name for the selected country (or a "no dominant org" fallback), and reverting to the original view when the lens changes away from Org.

## Approach

Create a `OrgLensCountryView` plain C# class backed by a new UXML template (`OrgLensCountryInfo.uxml`) and USS file (`OrgLensCountryInfo.uss`). Register the template in `HUD.uxml` as a sibling of the existing `country-info` instance, naming it `org-lens-country-info`. In `HUDDocument`, instantiate `OrgLensCountryView` from the new root element, subscribe `OrgMapState.PropertyChanged` alongside the existing lens and country subscriptions, and extend `RefreshCountryViews()` to toggle visibility between the two views based on the current lens. Dominant-org name resolution follows the spec data flow: look up `SelectedCountry.CountryId` in `OrgMapState.Entries` to get `TopOrgId`, then match that against `SelectedControl.OrgEntries` to find the `DisplayName`.

## Agent Steps

- [x] **Create UXML template** — write `Assets/UI/HUD/OrgLensCountryInfo/OrgLensCountryInfo.uxml` with a root `VisualElement` named `org-lens-country-bar` containing a `Label` named `org-name` (for the dominant org display name) and a `Label` named `org-no-dominant` (for the fallback message). Reference `SharedStyles.uss` and the new `OrgLensCountryInfo.uss`.

- [x] **Create USS file** — write `Assets/UI/HUD/OrgLensCountryInfo/OrgLensCountryInfo.uss` with `.org-lens-country-bar` layout styles (position, sizing, flex) matching the visual footprint of `.country-bar` from `CountryInfo.uss`. Keep colour and typography to shared classes only.

- [x] **Register template in HUD.uxml** — edit `Assets/UI/HUD/HUD.uxml` to add:
  1. A `<ui:Template>` declaration for `OrgLensCountryInfo` pointing to the new UXML file.
  2. A `<ui:Instance>` element named `org-lens-country-info` with class `org-lens-country-info-panel` placed immediately after the `country-info` instance inside `hud-root`.

- [x] **Create OrgLensCountryView.cs** — write `Assets/Scripts/Unity/UI/OrgLensCountryView.cs` as a plain C# class (no MonoBehaviour) in namespace `GS.Unity.UI`. Constructor receives the root `VisualElement`. Queries `org-name` and `org-no-dominant` labels. Exposes `Refresh(SelectedCountryState country, OrgMapState orgMap, CountryControlState control)`:
  - If `!country.IsValid` or lens is not Org (caller controls visibility from outside), hide root with `DisplayStyle.None`.
  - Look up `orgMap.Entries` for an entry whose `CountryId == country.CountryId`.
  - If found: resolve `DisplayName` from `control.OrgEntries` where `OrgId == entry.TopOrgId`; show `org-name` label with the name (or `entry.TopOrgId` as fallback if no matching entry); hide `org-no-dominant`.
  - If not found: hide `org-name`; show `org-no-dominant` label.
  - Show root with `DisplayStyle.Flex`.

- [x] **Wire OrgLensCountryView into HUDDocument.cs** — edit `Assets/Scripts/Unity/UI/HUDDocument.cs`:
  1. Add field `OrgLensCountryView _orgLensCountryView;`.
  2. In `Awake()`, after `_countryInfoRoot` is assigned, query `root.Q("org-lens-country-info")` and construct `_orgLensCountryView` from it (initially hidden).
  3. In `OnEnable()`, add `_state.OrgMap.PropertyChanged += HandleOrgMapChanged;`.
  4. In `OnDisable()`, add `_state.OrgMap.PropertyChanged -= HandleOrgMapChanged;`.
  5. Add `void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();`.
  6. Extend `RefreshCountryViews()`: determine `bool isOrgLens = _state.MapLens.Lens == MapLens.Org;`. When `isOrgLens`, hide `_countryInfoRoot` (set `DisplayStyle.None`) and call `_orgLensCountryView.Refresh(...)`. When not `isOrgLens`, ensure `_orgLensCountryView` root is hidden (`DisplayStyle.None`) and let the existing `_countryInfo.Refresh(...)` call show/hide `_countryInfoRoot` as normal (the existing `_orgPanelOpen` guard must remain intact for that path).
  7. Update `HandleLensChanged` to also call `RefreshCountryViews()` so the switch between lens views is immediate.

- [x] **Refresh Unity and verify** — call `refresh_unity`, then `read_console(types=["error"])` to confirm no compilation errors before closing.

## User Steps

### 1. Verify in Play Mode

Enter Play mode, open the map in Org lens, click several countries, and confirm:
- The org name panel appears and CountryInfo is hidden.
- Switching to Political lens restores CountryInfo.
- Clicking a country with no org entry shows the fallback message.
- No console errors.

## Constitution Check

| Principle | Status |
|---|---|
| Unity 6 + URP only | No rendering changes — not applicable. |
| ECS for game logic in `src/` | No ECS or `src/` changes. All new code is pure UI presentation. |
| VContainer is sole DI mechanism | No new DI registrations needed. `HUDDocument` already has all required state injected. No `FindObjectOfType` or `new` for services. |
| UI Toolkit only | New view uses UXML + USS + C# View class. No Canvas/UGUI components introduced. |
| Plan before implement | This plan file is being created before any code changes. |
| Spec before plan | Spec exists at `Docs/Specs/26_06_15_10_org-lens-selection-panel/spec.md`. |
| File organisation | Plan saved to `Docs/Specs/26_06_15_10_org-lens-selection-panel/plan.md` matching the dated spec identifier. |
| One `.asmdef` per feature folder | No new feature folder; all C# goes into `Assets/Scripts/Unity/UI/` which already has `GS.Unity.UI.asmdef`. |
| C# code style | Plan calls for tabs, `_` prefixes, always-braces, no redundant modifiers — all enforced in the implementation steps. |

Use /implement to start working on the plan or request changes.
