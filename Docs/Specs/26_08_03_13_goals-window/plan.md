# Plan: Goals Window

## Spec

Source: `Docs/Specs/26_08_03_13_goals-window/spec.md` (approved; owner clarifications baked in).

**Intent.** Add a HUD-opened Goals modal that lists every organization (score-sorted like Leaderboard orgs) and, for the selected org, shows live progress toward each configured win condition (total control 80%, fully control 15 countries, score goal), so the player can track how close any org is to winning without leaving the map HUD.

**Acceptance criteria (summary).**
- HUD gains a "Goals" button immediately right of Leaderboard; pointer-up opens a modal with Leaderboard-matching chrome (Goals header + X close) and `ModalState` blocking.
- Two columns: left = all orgs with scores (no filter); right = one progress row per win condition as `description [ N/M ]` with fill capped at 100%.
- Player org selected by default on open and every reopen; row selection uses a tab-active-class-toggle idiom (`.goals-row--selected`).
- Closing clears modal ownership like Leaderboard; available whenever Leaderboard is (no extra phase gate).

Verified config at plan time (`Assets/Configs/game_settings.json`): `total_control` 0.8, `full_control_countries` 15, `score_goal` **270000**, `maxControlPool` 100.

## Goal

Ship a read-only UI Toolkit Goals modal backed by a new `GoalsState` projection in `src/Game.Main`, with reusable current/target accessors extracted from the three completion-condition classes so UI math cannot drift from win evaluation.

## Approach

### 1. Extract current/target accessors from completion conditions

Today `TotalControlCondition`, `FullControlCondition`, and `ScoreGoalCondition` only expose `IsMet`. Extract the shared math into public instance methods (no new gameplay rules):

| Class | `GetCurrent(context)` | `GetTarget(context)` |
|---|---|---|
| `TotalControlCondition` | Sum of org control across `AvailableCountryIds` (same loop as `IsMet`) | `_threshold * availableCount * MaxControlPool` |
| `FullControlCondition` | Count of countries with control `>= MaxControlPool` | `_requiredCountryCount` |
| `ScoreGoalCondition` | `ResourceQuery.GetValue(..., OrgScore)` | `_goal` |

Refactor each `IsMet` to early-out on empty countries (existing behavior for control leaves) then `return GetCurrent(context) >= GetTarget(context)` so win checks and progress bars share one code path.

Also expose control-map overloads used by the Goals projector’s single-scan path:
- `TotalControlCondition.GetCurrentFromControl(IReadOnlyDictionary<string, int> control)` — sum of map values
- `FullControlCondition.GetCurrentFromControl(IReadOnlyDictionary<string, int> control, int maxControlPool)` — count of entries `>= maxControlPool`

Do **not** change `ICompletionCondition` itself (factory/`Any` stay boolean-only). Goals projection uses cached leaf descriptors (see §3) and calls the concrete accessors.

### 2. `GoalsState` on `VisualState`

Add to `src/Game.Main/VisualState.cs` (selection stays Unity-local, like Leaderboard tabs):

```csharp
public class GoalProgressEntryState {
	public WinConditionHintKind Kind { get; }
	public double ConfigValue { get; }   // raw config threshold for localized description args
	public double Current { get; }
	public double Target { get; }
	public int AvailableCountryCount { get; }
}

public class GoalsOrgEntryState {
	public string OrgId { get; }
	public IReadOnlyList<GoalProgressEntryState> Goals { get; }
}

public class GoalsState : INotifyPropertyChanged {
	public IReadOnlyList<GoalsOrgEntryState> Organizations { get; private set; }
	public void Set(List<GoalsOrgEntryState> organizations); // equality-gated via StateEquality
}
```

Wire `public GoalsState Goals { get; } = new GoalsState();` on `VisualState`. Store `Kind` + `ConfigValue` + `AvailableCountryCount` instead of a baked English `Description` — Unity formats text with existing `select_org.win_conditions.*` keys (same as `SelectOrgDocument.FormatGoalHintRow`).

**N/M units (locked):** absolute values matching `IsMet` math — control points / full-country count / org score — with shared `Target` per leaf across all orgs. Fill ratio = `min(1, Current / Target)` when `Target > 0`, else empty.

### 3. `GoalsProjector` + converter hook

Add `src/Game.Main/GoalsProjector.cs` (static helpers + leaf descriptor type, mirror `WinConditionHintProjector` + `SelectedWarProjector` style):

- Flatten `CompletionConditionConfig` the same way as `WinConditionHintProjector` (`any` expands members; unsupported types skipped) into **cached leaf descriptors** (kind + config value + constructed concrete condition instance). Rebuild the cache only when the converter is constructed (config is immutable for a session) — do **not** re-flatten or re-construct conditions every `UpdateGoals` tick.
- For each organization entity in the world (same org iteration style as `VisualStateConverter.UpdateLeaderboards`), build a `CompletionConditionContext` with `GameCompletionSystem.GetAvailableCountryIds(world)` and `MaxControlPool`.
- **Per org per tick, call `OrgMetrics.GetControlByCountry` at most once.** Derive both total-control and full-control currents from that shared map via the leaf conditions' control-map overloads (`GetCurrentFromControl`); score leaves still use `GetCurrent(context)`. Targets come from `GetTarget(context)` (shared across orgs for a given leaf).
- Preserve leaf order from config flatten (matches select-org hint order: total → full → score under current `game_settings.json`).

Extend `VisualStateConverter`:
- Constructor gains `CompletionConditionConfig? completionCondition` and `int maxControlPool` (defaults: null / 100-compatible fallback only if needed for existing tests).
- Cache flattened goal leaf descriptors from `completionCondition` in the ctor.
- Call `UpdateGoals(world)` from `Update(...)` after `UpdateLeaderboards(world)`.
- `GameLogic` construction already has `settings.CompletionCondition` and `MaxControlPool` — pass them into the converter ctor alongside existing args.

Add `StateEquality` helpers for the new entry types so identical projections do not raise `PropertyChanged`.

### 4. HUD entry button

In `Assets/UI/HUD/HUD.uxml`, inside `top-left-panel`, add `<ui:Button name="btn-goals" ... class="gs-btn leaderboard-hud-button"/>` as the sibling **immediately after** `btn-leaderboard`. Reuse `.leaderboard-hud-button` spacing (or a shared class alias) in `HUD.uss` — no new PanelSettings.

In `HUDDocument.cs`:
- Inject `GoalsWindowDocument` next to `LeaderboardWindowDocument`.
- Query `btn-goals`; `clicked` → `_goalsWindow?.Show()`.
- Localize via `hud.goals` in the same place as `RefreshLeaderboardButtonText` / locale refresh.

### 5. Modal UXML / USS / Document / View

New assets:
- `Assets/UI/Modal/GoalsWindow/GoalsWindow.uxml`
- `Assets/UI/Modal/GoalsWindow/GoalsWindow.uss`

Shell mirrors `LeaderboardWindow.uxml`: `gs-blackfade`, panel, centered title `goals-title`, absolute `btn-close` ("X"). Content is a horizontal split:
- Left: table header + `ScrollView` `goals-org-list` (place / flag / name / score rows, dynamic `CreateRow` like `LeaderboardWindowView`, org flags via `OrgVisualConfig`).
- Right: `ScrollView` `goals-progress-list` of rows: description label + track `VisualElement` with inner fill (width `%`) + `N/M` label. No Countries tab.

Scripts (existing `GS.Unity.UI` assembly — no new asmdef):
- `Assets/Scripts/Unity/UI/GoalsWindowDocument.cs` — `[RequireComponent(typeof(UIDocument))]`, `SortingOrder = 505` (distinct from Leaderboard `500` and WarProgress `510`; below FlyText `1000`), `Show`/`Hide` with `_ownsModalState` + `ModalState.IsModalOpen`, close via `PointerUpEvent` + `ContainsPoint`, subscribe to `Goals` + `Leaderboard` + `Locale` + `PlayerOrganization` while visible. **Locale handler only calls `RefreshTexts()` / refreshes the view — never `_loc.SetLocale` (HUD owns locale), matching Leaderboard.**
- `Assets/Scripts/Unity/UI/GoalsWindowView.cs` — owns `_selectedOrgId`; `ResetToPlayerOrg(playerOrgId)` on every `Show`; row activate via `PointerUpEvent` + `ContainsPoint` (not `Button.clicked`); on every left-list rebuild **re-apply `.goals-row--selected` from `_selectedOrgId` and preserve `scrollOffset`** (live refresh must not wipe selection chrome or jump scroll); refresh left from `Leaderboard.Organizations`, right from matching `Goals.Organizations` entry; format descriptions like `SelectOrgDocument`; format N/M with invariant ints for control/country goals and `ScoreFormat` for score; clamp fill to 100%.

### 6. Scene + DI

- `GameLifetimeScope`: `builder.RegisterComponentInHierarchy<GoalsWindowDocument>();` next to Leaderboard/WarProgress registrations.
- `Assets/Scenes/Map.unity`: add root `GoalsWindowUI` GameObject with `GoalsWindowDocument` + `UIDocument` (UXML = GoalsWindow, PanelSettings = same `HUDPanelSettings` guid as LeaderboardWindowUI `a52ac28cceb58ba4db172389975ccca7`), and register its Transform in `SceneRoots`. Prefer Unity Editor / MCP; YAML fallback is allowed **only with new unique fileIDs** (e.g. `9600000`–`9600003`) — **do not reuse Leaderboard’s `9300000`–`9300003` (or WarProgress/WarResult blocks)**; colliding IDs would break those scene objects.

### 7. Localization

Add to `en.asset` + `ru.asset` via the `localization` skill (real Russian, not English placeholders):
- `goals.title` → "Goals"
- `hud.goals` → "Goals"

Reuse existing description keys (no new copy unless a format needs a Goals-specific variant):
- `select_org.win_conditions.total_control`
- `select_org.win_conditions.full_control_countries`
- `select_org.win_conditions.score_goal`

## Agent Steps

- [x] **Extract GetCurrent/GetTarget on leaf conditions** — Update `src/Game.Systems/TotalControlCondition.cs`, `FullControlCondition.cs`, and `ScoreGoalCondition.cs`; keep `IsMet` behavior identical via the new accessors.

- [x] **Add Goals visual-state types + equality** — `GoalProgressEntryState`, `GoalsOrgEntryState`, `GoalsState` on `VisualState`; equality helpers in `StateEquality.cs`.

- [x] **Implement GoalsProjector + converter wiring** — New `src/Game.Main/GoalsProjector.cs`; extend `VisualStateConverter` ctor/`Update`/`UpdateGoals`; pass `CompletionCondition` + `MaxControlPool` from `GameLogic`.

- [x] **Core tests for accessors + projector** — Extend `CompletionConditionTests` / `ScoreGoalConditionTests` for current/target; add `GoalsProjectorTests` (and converter coverage if needed) per Tests section.

- [x] **GoalsWindow UXML/USS** — Create `Assets/UI/Modal/GoalsWindow/` two-column modal mirroring Leaderboard chrome and shared modal classes.

- [x] **GoalsWindowDocument + GoalsWindowView** — Modal lifecycle (`SortingOrder` 505, ModalState, PointerUp close), default/reopen player-org selection, live refresh, progress-bar fill + N/M.

- [x] **HUD button + document wiring** — `btn-goals` in `HUD.uxml`/`HUD.uss`; inject/open from `HUDDocument`; localize `hud.goals`.

- [x] **DI + Map scene UIDocument** — Register in `GameLifetimeScope`; add `GoalsWindowUI` to `Map.unity` with HUDPanelSettings.

- [x] **Localization keys** — Add `goals.title` / `hud.goals` to EN+RU via localization skill.

- [x] **Validate** — `dotnet test src/GlobalStrategy.Core.sln`; Release build if `src/` DLL plugins need refresh; Unity import / console clean.

## User Steps

These steps require Unity Editor scene/asset work, visual inspection in the Editor, or other hands-on Unity steps.

### 1. Confirm Map scene UIDocument

Open `Map.unity`, select `GoalsWindowUI`, and verify `UIDocument` references `GoalsWindow.uxml` and the same `HUDPanelSettings` as Leaderboard. Enter Play mode and confirm no missing-panel / null-root errors.

### 2. HUD open / close / modal blocking

Confirm the Goals button sits immediately right of Leaderboard, opens the modal above the HUD, X close restores map interaction without pausing simulation, and map clicks are blocked while open (`ModalState`).

### 3. Selection defaults and org switching

On first open and after close/reopen with another org previously selected, confirm the player org row is selected and the right panel shows that org’s bars. Click another org and confirm selection class + bars fully replace (no stale rows).

### 4. Progress bar visuals

With zero / partial / met-or-exceeded progress (use debug force-completion or time advancement), confirm unfilled / proportional / capped-full fills and correct N/M for all three goals, including live updates while the window stays open.

### 5. Left-column parity vs Leaderboard Organizations

With both windows openable in the same session, confirm the Goals left column matches the Leaderboard Organizations tab for the same tick: same orgs (no eliminated filter), same sort/places, same flags/names/scores. Catch presentation drift early.

## Tests

- `src/Game.Tests/CompletionConditionTests.cs` / `ScoreGoalConditionTests.cs` — `GetCurrent`/`GetTarget` match existing `IsMet` boundary cases (0.8 total control inclusive, 15 full countries, score goal at/under threshold); empty available countries → current 0 / safe target behavior consistent with `IsMet`.
- `src/Game.Tests/GoalsProjectorTests.cs` (new) — flattens `any` members into three rows; per-org currents differ while targets are shared; org list covers all seeded orgs; identical `Set` does not notify; fill inputs satisfy `Current/Target` for partial and over-target cases.
- Optional thin `VisualStateConverter` test if projector is only reachable through `UpdateGoals` with injected config (mirror `VisualStateConverterLeaderboardTests` seeding style).
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`. UI pointer/layout covered by User Steps (no Unity UI test harness).

## Tech Notes

- Leaderboard precedent: `LeaderboardWindowDocument` (`SortingOrder` 500, ModalState ownership), `LeaderboardWindowView.CreateRow` + `.leaderboard-tab--active`, HUD `btn-leaderboard` in `top-left-panel`, scene object `LeaderboardWindowUI`.
- Win data today: conditions in `src/Game.Systems/*Condition.cs`; `WinConditionHintProjector` is static thresholds only; live scores already on `VisualState.Leaderboard`; player org on `VisualState.PlayerOrganization.OrgId`.
- Progress-bar fill precedent: percentage `Length` width on a child fill element (`WarProgressLayoutBinder.UpdateProgressBar`), adapted to a single 0–100% track for goals.
- Do not modify Leaderboard behavior, select-org hint UI, or completion evaluation semantics beyond accessor extraction.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material/camera changes.
- **ECS game logic in `src/`** — current/target math and projection stay in `Game.Systems` / `Game.Main`; Unity documents only bind state and handle input.
- **VContainer sole DI** — `RegisterComponentInHierarchy<GoalsWindowDocument>()` + `[Inject] Construct`; no service locators.
- **UI Toolkit only** — UXML/USS + Document/View pair; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under `Docs/Specs/26_08_03_13_goals-window/` after the approved spec.
- **File organisation / assemblies** — UI stays in `Assets/Scripts/Unity/UI`; core types in existing `src` projects; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
