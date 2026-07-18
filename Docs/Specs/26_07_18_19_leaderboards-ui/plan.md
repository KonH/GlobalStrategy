# Plan: Leaderboards UI

## Spec

Source: `Docs/Specs/26_07_18_19_leaderboards-ui/spec.md`.

**Intent.** Add a fullscreen Leaderboard window, opened from the map HUD, with a `Leaderboard` header, close `X`, two tabs (`Organizations`, `Countries`), and a scrollable row list shaped as `place_number flag name score`. The window must keep updating while open and must use score sources that survive the resource-collector-pipeline migration: organization scores from the existing organization score source, country scores through the stable `CountryScoreSystem.GetScore(world, countryId)` path or a visual-state field populated from that path.

**Important dependency.** The resource collector pipeline (`Docs/Specs/26_07_18_17_resource-collector-pipeline/`) moves country score storage from `Country + Score` to `Resource{ResourceId="country_score"}` while preserving `CountryScoreSystem.GetScore`. This plan deliberately removes UI/visual-state dependence on direct `Country + Score` scans for country leaderboards. Organization score remains composed on `Organization + Score` and can be read through `OrgScoreSystem.GetScore` or an equivalent converter query.

## Goal

Expose current organization and country scores in a single modal UI without adding game rules, score recomputation, or save data. The implementation should add only presentation-facing visual-state projections and Unity UI Toolkit assets/scripts, while preserving ECS/game-logic ownership of score calculations.

## Plan Review Findings

The first plan pass was too loose in three implementation-sensitive areas. This revision fixes them before `/implement`:

1. **Country display names and tie-breaking need a source in core.** `Country` components only carry `CountryId`, so the converter must receive `CountryConfig` for deterministic country display-name fallback/sorting rather than assuming `Country.DisplayName` exists. Unity can still render localized `country_name.<id>` labels.
2. **The leaderboard modal must block map input without pausing simulation.** Unlike `GameMenuDocument`, `LeaderboardWindowDocument.Show()` must set `ModalState.IsModalOpen = true` but must not push `PauseCommand`; `Hide()` must clear modal state only if it owns the open modal, and must not push `UnpauseCommand`.
3. **Scene/DI wiring is required, not optional.** The implementation must add the modal `UIDocument` to `Assets/Scenes/Map.unity` following existing modal documents and register `LeaderboardWindowDocument` in `GameLifetimeScope`.

## Approach

### 1. Visual-state data model

Add leaderboard-specific projection types to `src/Game.Main/VisualState.cs`:

```csharp
public class LeaderboardEntryState {
	public int Place { get; }
	public string EntityId { get; }
	public string DisplayName { get; }
	public double Score { get; }
}

public class LeaderboardState : INotifyPropertyChanged {
	public IReadOnlyList<LeaderboardEntryState> Organizations { get; private set; }
	public IReadOnlyList<LeaderboardEntryState> Countries { get; private set; }
	public void Set(List<LeaderboardEntryState> organizations, List<LeaderboardEntryState> countries);
}
```

Add `public LeaderboardState Leaderboard { get; } = new LeaderboardState();` to `VisualState`. Keep the selected tab as Unity-view state, not core visual state, because tab selection is presentation-only and should not affect simulation snapshots or converter output.

Why a dedicated projection rather than ad hoc UI scans:
- Both tabs need sorted, place-numbered data.
- The open window must refresh naturally through the existing `VisualState` update cadence.
- Country score source compatibility belongs in one converter path, not inside the view class.
- `DisplayName` is a deterministic fallback/sort label. Country rows may still render localized `country_name.<countryId>` text in the Unity view.

### 2. VisualStateConverter score projection

Extend `src/Game.Main/VisualStateConverter.cs` with `UpdateLeaderboards(IReadOnlyWorld world)` and call it from the main conversion/update flow after `UpdateCountryScore(world)`. Update the `VisualStateConverter` constructor and `GameLogic` construction call to pass `CountryConfig` (and `OrganizationConfig` only if needed), because country display names are not present on `Country` components.

Organization entries:
- Iterate available organization entities or configuration-backed organization list consistently with other visual-state code.
- Read scores using `OrgScoreSystem.GetScore(world, orgId)` (preferred for storage encapsulation) or by scanning `Organization + Score` in the converter if the surrounding converter style makes that cleaner.
- Use the same display-name source already used for `PlayerOrganizationState` / org UI.

Country entries:
- Iterate available country entities or `CountryConfig.Countries` consistently with existing country visual-state code.
- Use `CountryConfig.FindByCountryId(countryId)?.DisplayName` as the deterministic `DisplayName` fallback and sort label; if absent, fall back to `countryId`.
- Read scores using `CountryScoreSystem.GetScore(world, countryId)`.
- Also update existing `CountryScoreState` to use `CountryScoreSystem.GetScore` instead of the current direct `Country + Score` scan so *all* visual-state country score consumers survive the resource-collector-pipeline migration.

Sorting:
- Sort each tab by score descending.
- Break ties by `DisplayName` ascending using `StringComparer.Ordinal`, then stable id ascending using `StringComparer.Ordinal`.
- Assign `Place` after sorting as consecutive 1-based row numbers.

Formatting remains a UI concern: the state stores raw `double` scores.

### 3. HUD entry button

Modify `Assets/UI/HUD/HUD.uxml` to add a leaderboard button to an existing top-level HUD control area. Preferred placement: inside `top-right-panel`, near the existing `Time` instance, because the top-right block already hosts global game controls and is managed by `HUDDocument`.

Implementation notes:
- Name the button `btn-leaderboard`.
- Use existing shared button classes (`gs-btn`, small/icon variant if one exists) instead of new one-off styling.
- Text can be `Leaderboard` for the first implementation; if there is an existing icon-button convention, `/implement` may choose an icon plus tooltip, but the spec requires discoverability.

Update `Assets/UI/HUD/HUD.uss` only for positioning/spacing needed by the new button. Do not create a new PanelSettings asset.

### 4. Modal asset files

Add a new modal folder following existing modal structure:

- `Assets/UI/Modal/LeaderboardWindow/LeaderboardWindow.uxml`
- `Assets/UI/Modal/LeaderboardWindow/LeaderboardWindow.uss`

UXML structure:
- Root hidden by default by the owning `UIDocument` script, like existing modal documents.
- Fullscreen `.gs-modal-root` / shared modal classes where compatible with existing modal USS.
- Header row:
  - `Label` named `leaderboard-title`, text/localized value `Leaderboard`.
  - `Button` named `btn-close`, text `X` or existing close-button styling.
- Tab row:
  - `Button` named `tab-organizations`, text `Organizations`.
  - `Button` named `tab-countries`, text `Countries`.
- Content:
  - `ScrollView` named `leaderboard-list`.
  - Empty-state `Label` named `leaderboard-empty`, text `No leaderboard entries available`, hidden when rows exist.

USS should mirror project modal visuals (`Assets/UI/Modal/SettingsWindow/`, `LoadWindow/`, `GameMenu/`) and shared styles:
- Fullscreen darkened modal backdrop.
- Centered or mostly-fullscreen content panel.
- Header with close button aligned right.
- Tab selected/unselected classes.
- Row class with columns for place number, flag, name, score.
- Vertical scrolling only; no horizontal scrolling.

### 5. Unity document/view classes

Add `Assets/Scripts/Unity/UI/LeaderboardWindowDocument.cs` as the `MonoBehaviour` attached to the new modal `UIDocument` GameObject.

Responsibilities:
- Inject `VisualState`, `ILocalization`, `CountryVisualConfig`, and `OrgVisualConfig`.
- In `Awake`, query root elements, construct `LeaderboardWindowView`, and hide the document root.
- In `OnEnable`, subscribe to:
  - `_state.Leaderboard.PropertyChanged`
  - `_state.Locale.PropertyChanged`
- In `OnDisable`, unsubscribe.
- `Show()` displays the root, sets `ModalState.IsModalOpen = true`, sets the default tab (`Organizations` unless an existing modal/tab convention requires preservation), and refreshes. It must not push `PauseCommand` or otherwise change simulation time.
- `Hide()` hides the root and clears `ModalState.IsModalOpen` when the leaderboard owns the open modal. It must not push `UnpauseCommand`.
- `IsVisible` mirrors other modal documents.
- Close button calls `Hide()`.

Add `Assets/Scripts/Unity/UI/LeaderboardWindowView.cs` as a plain view/helper class, following existing `*View` patterns (`TimeView`, `ActionLogView`, `ResourcesView`, etc.).

Responsibilities:
- Own a private selected-tab enum or boolean in Unity code (`Organizations` by default); do not add selected-tab state to `VisualState`.
- Wire tab button callbacks.
- Render rows into `leaderboard-list` from `VisualState.Leaderboard.Organizations` or `.Countries`. For country rows, display `_loc.Get($"country_name.{entry.EntityId}")` when it resolves, falling back to `entry.DisplayName`; for organization rows, display `entry.DisplayName`.
- Reuse existing `VisualElement` row construction and class-list patterns; no retained ECS/world references.
- Set flag backgrounds through `OrgVisualConfig.Find(orgId)?.flag` or `CountryVisualConfig.Find(countryId)?.flag`, matching existing UI fallback behavior.
- Format scores consistently. If no shared formatter exists, use one local helper with invariant culture that renders whole numbers without decimals and non-whole values with at most one decimal digit (`Math.Abs(value % 1) < epsilon ? "0" : "0.0"`).
- Preserve selected tab while refreshing. Preserve scroll offset where practical by storing/restoring `ScrollView.scrollOffset` after rebuilding rows.

### 6. HUDDocument wiring

Update `Assets/Scripts/Unity/UI/HUDDocument.cs`:
- Inject `LeaderboardWindowDocument` in `Construct`.
- Query `btn-leaderboard` in `Start`.
- Register click handler: if the modal exists and is not visible, call `Show()`; if it is already visible, call `Show()` or a new `Focus()` method without duplicating it.
- No command should be pushed; this is presentation-only.

Update `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` to register `LeaderboardWindowDocument` with `builder.RegisterComponentInHierarchy<LeaderboardWindowDocument>();`, matching `GameMenuDocument`, `SettingsWindowDocument`, and `OrgInfoDocument`.

Update `Assets/Scenes/Map.unity` to add a `UIDocument` GameObject for the leaderboard modal, using the new `LeaderboardWindow.uxml` and the same modal PanelSettings/sorting pattern as existing modal documents. Do not create the modal dynamically in code.

### 7. Localization

If surrounding modal/HUD labels are localized, add keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`:
- `leaderboard.title`
- `leaderboard.tab.organizations`
- `leaderboard.tab.countries`
- `leaderboard.empty`
- optionally `hud.leaderboard` / tooltip key if the HUD button uses a localized label/tooltip.

If the surrounding UI still uses literal text for comparable labels, keep literals in UXML and defer broader localization cleanup.

### 8. Tests

Core tests (`src/Game.Tests/`):
- Add `LeaderboardState` / `VisualStateConverter` tests that seed countries and organizations with scores, pass `CountryConfig` into the converter, run the converter, and assert:
  - organization rows are sorted descending by score;
  - country rows are sorted descending by `CountryScoreSystem.GetScore` values;
  - tie-breaking is deterministic by config/display name then id;
  - place numbers are 1-based and consecutive after sorting.
- Add/adjust a `VisualStateConverter` or `CountryScoreState` test verifying country scores are populated through `CountryScoreSystem.GetScore` semantics, not a direct `Country + Score` dependency. If the resource-collector-pipeline branch has already landed, this should be tested with `country_score` resource-backed storage; if not, keep the test against the current `GetScore` behavior and avoid asserting storage details.

Unity-side tests are optional unless the project already has UIToolkit edit-mode tests. If such tests exist, add coverage for:
- clicking the HUD button calls `Show()` on the leaderboard document;
- tab switching changes rendered data;
- refresh while open rebuilds rows and keeps the selected tab.

Manual/Unity checks:
- Enter Play mode and confirm the HUD button opens the fullscreen modal.
- Confirm `Organizations` and `Countries` tabs render flags/names/scores.
- Change score-affecting state (or use existing debug commands/time advancement) while the window remains open and confirm row scores/order update.
- Confirm close `X` returns to map interaction.

### 9. Build / import

After implementation:
- Run `dotnet test src/GlobalStrategy.Core.sln`.
- Run `dotnet build src/GlobalStrategy.Core.sln -c Release` if any `src/` changes affect the Unity plugin DLLs.
- Let Unity import changed UXML/USS/script assets and check the Console for errors.
- Because this is a perceptible web-app/game UI change, capture a screenshot of the opened leaderboard window after implementation.

## Steps

### Agent Steps

- [ ] Add `LeaderboardEntryState` and `LeaderboardState` to `src/Game.Main/VisualState.cs` (no selected-tab state in core).
- [ ] Extend `src/Game.Main/VisualStateConverter.cs` and its `GameLogic` construction call to populate leaderboards with country display names from `CountryConfig`, and update country-score projection through `CountryScoreSystem.GetScore`.
- [ ] Add/update core tests for sorting, place numbering, tie-breaking, and country-score source compatibility.
- [ ] Add `Assets/UI/Modal/LeaderboardWindow/LeaderboardWindow.uxml`.
- [ ] Add `Assets/UI/Modal/LeaderboardWindow/LeaderboardWindow.uss`.
- [ ] Add `Assets/Scripts/Unity/UI/LeaderboardWindowView.cs`.
- [ ] Add `Assets/Scripts/Unity/UI/LeaderboardWindowDocument.cs`.
- [ ] Add the `btn-leaderboard` entry point to `Assets/UI/HUD/HUD.uxml` and styling to `Assets/UI/HUD/HUD.uss` as needed.
- [ ] Wire `HUDDocument` to inject/open `LeaderboardWindowDocument`.
- [ ] Register `LeaderboardWindowDocument` in `GameLifetimeScope` and add its `UIDocument` to `Assets/Scenes/Map.unity` using the existing modal document pattern.
- [ ] Add localization keys if comparable UI labels are localized.
- [ ] Run core tests/builds, perform Unity import/play-mode sanity checks, and capture a screenshot of the new modal.

### User Steps

- [ ] Review the new button placement in the HUD and decide whether it should remain textual (`Leaderboard`) or become an icon/tooltip after first implementation.
- [ ] In Play mode, sanity-check that organization and country ordering matches expected current scores before and after a score update.
- [ ] Confirm the fullscreen modal styling matches the desired visual hierarchy relative to existing settings/load/game-menu modals.

## Tests

Primary automated command after implementation:

```bash
dotnet test src/GlobalStrategy.Core.sln
```

If core code changes require updated Unity plugins:

```bash
dotnet build src/GlobalStrategy.Core.sln -c Release
```

Recommended manual checks:

- Open the map HUD, click `Leaderboard`, verify the modal appears.
- Switch between `Organizations` and `Countries` tabs.
- Scroll a long list and verify row layout stays aligned.
- Advance time or trigger score-affecting debug/gameplay changes while the modal remains open and verify rows update.
- Close with `X` and verify map interaction resumes.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- **Unity 6 + URP only.** This plan changes UI Toolkit UXML/USS/scripts only; no render pipeline, shader, material, or camera-stack changes.
- **ECS for game logic in `src/`.** The only `src/` work is read-only visual-state projection of existing scores. Score formulas and game rules remain in ECS systems.
- **VContainer sole DI.** The modal document is registered via `RegisterComponentInHierarchy<LeaderboardWindowDocument>()` and injected through the existing VContainer scene/document pattern; no static mutable singleton or `FindObjectOfType` dependency is introduced.
- **UI Toolkit only.** The UI is UXML/USS plus UI Toolkit view/document classes; no Canvas/uGUI.
- **Plan before implement.** This plan is the implementation artifact for the existing spec before any code or asset implementation.
- **File organisation.** Plan lives beside its spec under `Docs/Specs/26_07_18_19_leaderboards-ui/plan.md`.
- **Assembly structure.** If new Unity scripts are added under `Assets/Scripts/Unity/UI/`, they remain within the existing UI assembly/folder pattern; no new feature folder assembly is required unless the current project convention for modal windows requires it.
- **C# style.** Implementation must use tabs, braces, `_`-prefixed private members, and no redundant access modifiers.

Use `/implement` to start working on this plan or request changes.
