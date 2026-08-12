# Plan: Tutorial (Part B)

## Spec

Source: `Docs/Specs/26_08_08_18_tutorial/spec.md` (approved; owner clarifications baked in — do not re-open Ambiguities). Builds on part A (`Docs/Specs/26_08_07_13_short-term-tasks/`) — same `TasksConfig` / `TaskProgressSystem` / `triggerCondition` / `PlayerTasksView` / `ActiveTasks` spine. No second task runtime.

**Intent.** Deliver sequential guided onboarding as tutorial-marked entries on the existing short-term tasks pipeline, with Settings gate + cross-session preference progress, highlight arrow, pause ownership UX, TimeSystem resume-on-speed, and authored tutorials 0–10.

**Acceptance criteria (summary).**
- Settings (Unity `SettingsWindowDocument` + Web `Settings.razor`): Tutorials checkbox default **on**; **Reset tutorials**; **Reset settings to default** (delete store, reload defaults, refresh UI). Persist via `SettingsStorage` / `AppPreferences`.
- Extend `TaskDefinition` with `isTutorial` / `highlightTargetId`; replace empty `tasks_config.json` with tutorials 0–10; locale keys via localization skill at implement.
- Exactly one tutorial active; open chain via `taskCompleted(prev)` ∧ ¬`tutorialTaskActive` ∧ `tutorialsEnabled`; tutorial 10 also requires `uiElementShown(none)` (chrome-clear).
- Cross-session: completed tutorial ids in app preferences; seed into world `TaskCompleted` on session start; sync on complete; disable-while-active → force-complete that step (no reward/close effects).
- Open auto-pauses only if player was unpaused (tutorial owns pause); complete/force-complete auto-unpauses only if ownership held; player resume never blocked and never re-paused for that step. Speed-click-while-paused resumes + applies multiplier.
- Tutorial accordion initially expanded; highlight arrow UITK overlay with back-and-forth motion keyed by `highlightTargetId` registry.
- Close facts: map pan/zoom AND (0), key (1/2/9), UI opens (3–6, 10), military advisor tooltip (5), `DrawCardsCommand` / `ReceiveCardCommand` (7–8). Highlight 6 = `actions-toggle-btn`.

## Goal

Author the tutorial content layer on part A’s runtime: preference-backed enable/progress, a `src/`-owned Triggers bag fed by presentation publishers + world/command facts, pause ownership + TimeSystem resume-on-speed, HUD auto-expand + highlight arrow — without forking task open/close/reward logic.

## Approach

### 1. Config — `IsTutorial` / `HighlightTargetId` + authored list 0–10

Extend `src/Game.Configs/TasksConfig.cs` `TaskDefinition`:

```csharp
public bool IsTutorial { get; set; }
public string HighlightTargetId { get; set; } = "";
```

Newtonsoft case-insensitive maps `isTutorial` / `highlightTargetId` from JSON (same as part A).

Replace `Assets/Configs/tasks_config.json` `{ "tasks": [] }` with the full tutorial list 0–10 from the spec (`tutorial_welcome_camera` … `tutorial_goals_window`). Empty `reward` / effect id lists. Conditions use **only** existing `triggerCondition` + `mul` / `eq` / `value` (no new ExpressionNode `type`s — product kinds map to trigger ids below).

**AND composition:** use `mul` of 0/1 members (existing). Example open for tutorial N (N≥1):

```json
{
  "type": "mul",
  "members": [
    { "type": "triggerCondition", "triggerId": "tutorialsEnabled" },
    { "type": "eq", "members": [
      { "type": "triggerCondition", "triggerId": "tutorialTaskActive" },
      { "type": "value", "value": 0 }
    ]},
    { "type": "triggerCondition", "triggerId": "taskCompleted:tutorial_<prev>" }
  ]
}
```

Tutorial 0 omits `taskCompleted:*`. Tutorial 10 adds `{ "type": "triggerCondition", "triggerId": "uiElementShown:none" }`.

Close examples:
- 0: `mul` of `mapPositionChanged` ∧ `mapZoomChanged`
- 1/2/9: `keyPressed`
- 3: `uiOpened:selectedCountryPanel`
- 4: `uiOpened:characterList`
- 5: `tooltipShown:militaryAdvisorTooltip`
- 6: `uiOpened:actionsPanel`
- 7: `commandTrigger:draw`
- 8: `commandTrigger:cardSelected`
- 10: `uiOpened:goalsWindow`

**HighlightTargetId values (config):** `player_org_panel`, `time_panel`, `characters_button`, `military_advisor_card`, `actions_button`, `action_deck`, `goals_button` (omit/empty when none). Registry in Approach §6 maps these to UITK elements.

**Localization (implement skill):** add EN + real RU for all `task.tutorial_*.name` / `.desc` plus Settings chrome keys (`settings.tutorials`, `settings.reset_tutorials`, `settings.reset_defaults`, checkbox label). Use `localization` skill — RU desc for task 9 exactly as owner string in spec.

Update `TasksConfigTests` sample to cover new fields; keep empty-list case for regression.

### 2. Expression facts / Triggers bag — publishers + world/command merge

**Do not invent a second condition runtime.** Keep part A `ExpressionContext.Triggers` + `triggerCondition`. Populate a mutable bag before each `TaskProgressSystem.Update`.

#### Trigger id convention

| Spec kind | Trigger id | Source |
|---|---|---|
| `tutorialsEnabled` | `tutorialsEnabled` | Preference → bag (session seed + toggle command) |
| `tutorialTaskActive` | `tutorialTaskActive` | World: 1 iff any active task’s def has `IsTutorial` |
| `taskCompleted` | `taskCompleted:{taskId}` | World completed set (includes preference-seeded entities) |
| `mapPositionChanged` | `mapPositionChanged` | Presentation latch |
| `mapZoomChanged` | `mapZoomChanged` | Presentation latch |
| `keyPressed` | `keyPressed` | Presentation latch (any key) |
| `uiOpened` | `uiOpened:{surface}` | Presentation level (see surfaces below) |
| `uiElementShown(none)` | `uiElementShown:none` | Presentation chrome-clear aggregate |
| `tooltipShown` | `tooltipShown:militaryAdvisorTooltip` | Presentation (`TooltipSystem` / CharactersView) |
| `commandTrigger` | `commandTrigger:draw` / `commandTrigger:cardSelected` | `GameLogic` from command queues **before** `TaskProgressSystem` |

UI surface ids: `selectedCountryPanel`, `characterList`, `actionsPanel`, `goalsWindow`.

#### `TaskTriggerBag` (new helper in `src/Game.Systems/`)

```csharp
public static class TaskTriggerBag {
	public static Dictionary<string, double> Build(
		World world,
		TasksConfig tasksConfig,
		IReadOnlyDictionary<string, double> presentationTriggers,
		bool tutorialsEnabled,
		string playerOrgId,
		bool drawCommandThisTick,
		bool receiveCardCommandThisTick);
}
```

Merge order each tick:
1. Copy presentation latch bag (map/key/ui/tooltip/chrome).
2. Set `tutorialsEnabled` ← preference flag (1/0).
3. Scan world `TaskActive`/`TaskCompleted` + config: set `tutorialTaskActive`, every `taskCompleted:{id}` for completed ids (prefer setting only ids referenced by config, or all completed — either works; recommend all completed tutorial ids + any completed id present in config for cheap eval).
4. Set `commandTrigger:draw` / `commandTrigger:cardSelected` from **this tick’s** player-org-only
   `ReadDrawCardsCommand` / `ReadReceiveCardCommand` (1 if any command with `OrgId == playerOrgId`),
   **before** `TaskProgressSystem` — leave `DrawCardSystem` order as-is; only the trigger read moves earlier.
   Bot/other-org draws must not close player tutorials.

#### Presentation publishers (Unity — Constitution-safe)

Own a session-scoped `TutorialPresentationTriggers` service as a plain class in `GS.Unity.Common`
(Map and UI can both reference Common; Map must not reference UI). Register it in VContainer; do **not** place it under `GS.Unity.UI`:

```csharp
public class TutorialPresentationTriggers {
	public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);
	public void Set(string id, double value) { Values[id] = value; }
	public void ClearEdge(string id) { Values.Remove(id); }
	public void ClearTaskEdges() { /* remove mapPositionChanged, mapZoomChanged, keyPressed */ }
}
```

Publishers (glue only — set bag entries; never open/close tasks):
- `MapCameraController`: on successful drag pan → `mapPositionChanged=1`; on scroll/pinch zoom change → `mapZoomChanged=1`.
- `TimeInputHandler` / HUD keyboard path / shared input: any key press while map session active → `keyPressed=1` (exclude pure UI text fields if any; default = any keyboard key).
- `HUDDocument` / `CountryInfoView`: when `SelectedCountry.IsValid` → `uiOpened:selectedCountryPanel=1` else 0; characters slide class `characters-slide--open` → `uiOpened:characterList`; actions slide `actions-slide--open` → `uiOpened:actionsPanel`.
- `GoalsWindowDocument`: visible → `uiOpened:goalsWindow=1`.
- Chrome-clear: set `uiElementShown:none=1` only when selected-country panel hidden, characters/actions slides closed, goals window hidden, card-draw modal not showing (`CardDrawAnimator` / equivalent), and other overlay chrome closed (org-info slides, war/result/destroyed modals as needed for “no other UI chrome”). Else 0.
- `TooltipSystem` / `CharactersView`: when trigger id matches `role-military_advisor-*` and tooltip visible → `tooltipShown:militaryAdvisorTooltip=1`; clear when hidden.

**Edge latch lifetime:** Presentation publishers latch `mapPositionChanged` / `mapZoomChanged` / `keyPressed` until cleared. `GameLogic` (not UITK) owns clearing those edge keys when the active tutorial id changes or becomes none — call `TutorialPresentationTriggers.ClearTaskEdges()` from the host/`GameLogic` after `TaskProgressSystem` (or immediately before Build on id change). `commandTrigger:*` are **same-tick** facts from player-org command reads (close evaluates in that tick); do not leave them latched across tutorials. Level facts (`uiOpened:*`, chrome-clear, tooltip) are continuous each tick.

**Web:** seed `tutorialsEnabled` + preference completions into `GameSession` the same way; presentation latch bag may start empty (Web map/HUD publishers optional follow-up if Web lacks parity UI — Settings + seeding still required). ConsoleRunner: bag empty except world-derived + preference flags for tests/automation.

#### Wire `GameLogic`

Today (`GameLogic.Update`):

```csharp
TaskProgressSystem.Update(..., triggers: null); // implicit
```

Change to hold `Dictionary<string, double> _presentationTriggers` (or inject `ITutorialPresentationTriggers` via context) + `bool _tutorialsEnabled`, expose setters/`SeedTutorialProgress` API used by Unity/Web hosts:

```csharp
var drawThisTick = HasPlayerOrgCommand(_commandAccessor.ReadDrawCardsCommand(), playerOrgId);
var receiveThisTick = HasPlayerOrgCommand(_commandAccessor.ReadReceiveCardCommand(), playerOrgId);
var triggers = TaskTriggerBag.Build(
	_world, _tasksConfig, _presentationTriggers, _tutorialsEnabled, playerOrgId,
	drawThisTick, receiveThisTick);
TaskProgressSystem.Update(..., triggers: triggers, progressSink: _tutorialProgressSink);
```

Keep existing call site after relation sync / `ReceiveCardSystem` and before `DrawCardSystem` / `GameCompletionSystem` / `VisualStateConverter`.

### 3. Preference store — enable, completed ids, seed, force-complete, resets

#### Unity `SettingsStorage` (`Assets/Scripts/Unity/Save/SettingsStorage.cs`)

Extend `SettingsData`:

```csharp
public bool TutorialsEnabled = true;           // default on when missing
public List<string> CompletedTutorialIds = new();
```

API: `TutorialsEnabled` get/set (persist); `IReadOnlyList<string> CompletedTutorialIds`; `MarkTutorialCompleted(string taskId)`; `ClearCompletedTutorials()`; `ResetToDefaults()` → delete `settings.json`, reload defaults (locale empty→`CustomLocalization` default, tutorials on, empty completed set).
**Autosave is not in `SettingsStorage` today** — on in-game reset also push `ChangeAutoSaveIntervalCommand` with `GameSettings.AutoSaveInterval` (or `"monthly"`) and refresh toggles from VisualState/AppSettings. On main menu, either persist autosave into `SettingsStorage` as part of this work or treat Unity main-menu autosave reset as N/A until persisted (Web already uses `AppPreferences`).

#### Web `AppPreferences` + `IPreferencesStore`

Keys: `gs.preferences.tutorialsEnabled` (`"true"`/`"false"`, default true when missing); `gs.preferences.completedTutorials` (JSON array of ids). Add `RemoveItem` to `IPreferencesStore` + `LocalStoragePreferencesStore` (`localStorage.removeItem`) for full reset. `ResetToDefaults()` removes known preference keys (locale, autosave, tutorials, completed) and reloads page-bound UI to defaults.

#### Session seed (world)

Seed **after the world exists for that session**, in both hosts:
- New game: after `InitSystem` returns true (first tick), before the first `TaskProgressSystem` open pass.
- Load/continue: immediately after `LoadSystem.Apply` / `GameLogic.LoadState` (`Apply` calls `DestroyAll`, so any pre-load seed is gone).
- Web `GameSession` start/load must mirror the same two hooks.

For each `taskId` in preference completed set that exists in `TasksConfig` with `IsTutorial` and is not already `TaskCompleted` in world: create entity `TaskId` + `TaskCompleted` (no `TaskActive`, no open effects).

Also set `_tutorialsEnabled` from preferences into `GameLogic`.

#### Sync on complete / force-complete API

Add `ITutorialProgressSink` in `src/` (e.g. `Game.Main` / `Game.Systems`). `GameLogic` passes it into `TaskProgressSystem.Update`.

Add `src/Game.Commands/SetTutorialsEnabledCommand.cs` (`bool Enabled`).

Process `SetTutorialsEnabledCommand` in `src` (thin system immediately before tasks, or inside `TaskProgressSystem`):
- Update enable flag used in Triggers merge.
- If `Enabled == false` and an active tutorial exists: **force-complete path** (not the normal close branch) — `TaskCompleted`, no close effects/rewards; `sink.MarkCompleted(taskId)`; clear pause ownership; highlight clears next VisualState pass.
- On **normal** tutorial close, also call `sink.MarkCompleted` (same sink).
- If enabling, no special action (next tick may open next incomplete tutorial).

Unity/Web register adapters that write `SettingsStorage` / `AppPreferences` only — no open/close rules in MonoBehaviours.

Unity Settings checkbox / Web toggle push this command when in-session; when on main menu only, write preferences directly (no world).

#### Settings UI

**Unity** `SettingsWindow.uxml` / `SettingsWindowDocument`:
- Tutorials row: checkbox or toggle button (match existing toggle styling) bound to `SettingsStorage.TutorialsEnabled`; in-game also push `SetTutorialsEnabledCommand`.
- Buttons: **Reset tutorials** → `ClearCompletedTutorials()` (+ flytext optional).
- **Reset settings to default** → `ResetToDefaults()`, refresh locale/autosave/tutorials UI, push locale/autosave/tutorial commands if a session is live so simulation matches.

**Web** `Settings.razor`: same three controls against `AppPreferences`; navigate-back unchanged. Extend `AppPreferencesTests`.

Main-menu Unity settings (`MainMenuLifetimeScope` already has `SettingsWindowDocument` + `SettingsStorage`): same storage instance pattern — ensure Tutorials checkbox works out of map scene without requiring `GameLogic`.

### 4. Pause ownership + speed-click-while-paused resumes

#### TimeSystem resume-on-speed (side change)

In `src/Game.Systems/TimeSystem.cs`, when applying `ChangeTimeMultiplierCommand`, if `time.IsPaused`, set `IsPaused = false` **and** apply `MultiplierIndex` (same interaction). Order relative to explicit Pause/Unpause: keep existing pause/unpause application first; then speed change may clear pause (spec: speed-click-while-paused resumes). Update `TimeSystemTests` with paused + speed → unpaused + new multiplier + time advances.

HUD `OnSpeedChange` / `TimeInputHandler` digit shortcuts already push `ChangeTimeMultiplierCommand` only — no Unity-side Unpause needed once TimeSystem owns the behaviour. Spacebar toggle stays as today.

#### Tutorial pause ownership (ECS in `src/`)

New marker component e.g. `TutorialOwnsPause` — **make it `[Savable]`** (or re-apply ownership after `LoadState` when an `IsTutorial` task is `TaskActive` and `GameTime.IsPaused`), so complete/force-complete still auto-unpauses after save/load mid-step.

On **tutorial open** inside `TaskProgressSystem` (when creating `TaskActive` for `IsTutorial`):
- If `!GameTime.IsPaused`: set `IsPaused = true`, add world singleton or task-entity `TutorialOwnsPause`.
- If already paused: do **not** add ownership.

On **player resume** in `TimeSystem`: clear any `TutorialOwnsPause` when `UnpauseCommand` is applied **or** when a `ChangeTimeMultiplierCommand` clears pause while paused. Do not re-pause later for that step.

On **tutorial close / force-complete**:
- If `TutorialOwnsPause` present: set `IsPaused = false`, remove marker.
- Else: leave pause as-is.

Never block `UnpauseCommand`. Never re-add pause after ownership cleared until a **later** tutorial opens while unpaused.

### 5. HUD — auto-expand + highlight arrow

#### VisualState

Extend `ActiveTaskEntryState` with `bool IsTutorial` and `string HighlightTargetId` (from config join in `VisualStateConverter.UpdateActiveTasks`). Update `StateEquality`.

#### `PlayerTasksView` auto-expand

On `Refresh`, capture `previous = _lastState` **before** `_lastState = state`. Then:
1. If a task with `IsTutorial` is in `state` but not in `previous`, set `_expandedTaskId` to that id.
2. Else if current `_expandedTaskId` is no longer in `state`, clear it.
3. Then rebuild list. Do not clear expansion after setting a newly appeared tutorial in the same Refresh.

Non-tutorial tasks remain collapsed-by-default. Preserve part A rule: while expanded, any header click only collapses.

#### Highlight arrow overlay

New UITK overlay under HUD root (e.g. `Assets/UI/HUD/TutorialHighlight/` UXML/USS + `TutorialHighlightView`):
- When active tasks include a tutorial with non-empty `HighlightTargetId`, resolve target `VisualElement` via registry, show arrow (USS image/rotation) aimed at target world-bound center (`element.worldBound.center`), continuous back-and-forth offset animation in `schedule.Execute` / `Transform` translate (presentation-only).
- Hide when no matching active tutorial / empty id / target missing.

**Registry** (`HighlightTargetId` → resolver):

| Id | Resolver |
|---|---|
| `player_org_panel` | HUD `player-country` / `.player-country-panel` |
| `time_panel` | `time-panel` |
| `characters_button` | `chars-toggle-btn` |
| `military_advisor_card` | Characters list card for role `military_advisor` (query via `CharactersView` / data attr / known name pattern) |
| `actions_button` | `actions-toggle-btn` (owner decision — not characters) |
| `action_deck` | `CountryActionsView.DeckPileElement` / `.action-deck-wrapper` |
| `goals_button` | `btn-goals` |

Wire from `HUDDocument`: construct view, subscribe `ActiveTasks.PropertyChanged`, pass element lookup callbacks that close over HUD queries / country views.

### 6. Wire `GameLogic` + hosts (summary)

- `GameLogic`: presentation trigger bag + tutorialsEnabled flag; `TaskTriggerBag.Build` each tick; pass into `TaskProgressSystem`; process `SetTutorialsEnabledCommand` / force-complete; seed API; tutorial pause ownership on open/close; preference sink hook.
- `GameLifetimeScope`: register `SettingsStorage` instance already created; register `TutorialPresentationTriggers` in `GS.Unity.Common`; inject into MapCamera / HUD / Settings; seed after InitSystem success and after every `LoadState`.
- Web: `AppPreferences` extensions; `GameSession` seed after init and after load; enable flag; Settings.razor controls; `IPreferencesStore.RemoveItem`.
- No new asmdef; trigger bag in Common; UI stays in `GS.Unity.UI`; Map stays in `GS.Unity.Map`.

### 7. Explicitly out of scope (do not implement)

Non-tutorial gameplay task packs; bot tutorial pursuit; multiplayer sync; new reward VFX; redesigning card-draw UX (#153 already shipped); changing part A accordion collapse-only rule beyond tutorial initial expand; new ExpressionNode operand types beyond `triggerCondition` + existing combinators.

## Agent Steps

- [x] **Extend TaskDefinition + author `tasks_config.json` 0–10** — `IsTutorial` / `HighlightTargetId`; full open/close trees via `triggerCondition` + `mul`/`eq`; empty rewards/effects; update `TasksConfigTests`.

- [x] **Localization** — EN + RU task name/desc + Settings chrome keys via `localization` skill (task 9 RU exact).

- [x] **TaskTriggerBag + GameLogic Triggers wiring** — Merge presentation/world/preference/command facts; pass bag into `TaskProgressSystem`; set `commandTrigger:*` from draw/receive reads before tasks.

- [x] **Presentation publishers (Unity)** — `TutorialPresentationTriggers` + MapCamera pan/zoom, key, UI open/chrome-clear, military advisor tooltip publishers; edge clear on active-tutorial change.

- [x] **Preferences + Settings UI (Unity + Web)** — `SettingsStorage` / `AppPreferences` tutorialsEnabled + completed ids; Reset tutorials; Reset settings to default (Unity in-game also resets autosave via command); UXML + `SettingsWindowDocument` + `Settings.razor`; `ITutorialProgressSink` + `SetTutorialsEnabledCommand` force-complete path (no rewards/effects); seed after InitSystem and after every LoadState.

- [x] **Pause ownership + TimeSystem resume-on-speed** — savable `TutorialOwnsPause` open/complete/player-resume (Unpause **or** speed-change) rules; TimeSystem clears pause on speed change; tests.

- [x] **HUD auto-expand + highlight arrow** — `ActiveTaskEntryState` fields; `PlayerTasksView` Refresh order (capture previous before assign; expand new tutorial before clear); `TutorialHighlightView` + target registry; USS animation.

- [x] **Core tests** — Per Tests section.
- [x] **Validate** — `dotnet test src/GlobalStrategy.Core.sln`; `/dotnet-build Release` after `src/` changes.

## User Steps

These steps require Unity Editor scene/asset work, visual inspection in the Editor, or other hands-on Unity steps the agent cannot perform.

### 1. Confirm tasks_config TextAsset on GameLifetimeScope

Open `Assets/Scenes/Map.unity`, select `GameLifetimeScope`, and confirm `_tasksConfigAsset` still references `Assets/Configs/tasks_config.json` (now non-empty). Enter Play mode and verify no missing-config errors; tutorial 0 should be eligible to open when preferences are default.

### 2. Settings window chrome

In Main Menu and in-game Settings, confirm Tutorials checkbox (default on), Reset tutorials, and Reset settings to default are visible, localized, and refresh the UI correctly after reset (Tutorials on, completed set cleared, locale/autosave defaults).

### 3. Tutorial happy path (0 → 10)

With Tutorials enabled and cleared completed set: start a new game. Confirm tutorial 0 auto-expands and auto-pauses if time was running; pan + zoom completes it; subsequent steps open one at a time with correct highlights (org panel, time panel, characters button, military advisor card, **actions** button, deck). Confirm 7–8 complete on draw / choose-card. Confirm 10 opens only when chrome is clear and completes on Goals window.

### 4. Highlight arrow animation

On a step with `highlightTargetId` (e.g. tutorial 1 or 6), confirm the arrow overlays the correct control and animates back-and-forth continuously; confirm it disappears on step complete / Tutorials off.

### 5. Pause ownership + speed resume

While a tutorial that auto-paused is active: press play/space — game resumes and stays unpaused for that step. While paused (tutorial or manual), click a speed modifier — game resumes at that speed. Complete a tutorial that still owns pause — game auto-unpauses.

### 6. Preference progress + disable mid-flight

Complete a few tutorials, quit to menu, start a new game — skipped steps stay completed. Disable Tutorials while a step is active — that step force-completes, highlight clears, no further tutorials open. Reset tutorials in Settings, then start a new game — chain restarts at 0.

## Tests

- `TasksConfig` — deserializes `isTutorial` / `highlightTargetId`; full tutorial JSON sample; missing optionals default false/"".
- `TaskTriggerBag` / expression — open mul trees require tutorialsEnabled ∧ ¬tutorialTaskActive ∧ taskCompleted; chrome-clear id; missing trigger → 0.
- `TaskProgressSystem` — tutorial mutual exclusion (second tutorial does not open while one active); sequencing 0→1; force-complete on `SetTutorialsEnabledCommand(false)` marks completed without rewards/effects; preference sink notified; non-tutorial tasks unaffected by tutorialsEnabled=0.
- Seeding — given preference completed ids, session seed creates `TaskCompleted` entities so those tutorials never re-open.
- Command triggers — player-org `DrawCardsCommand` / `ReceiveCardCommand` present this tick set close triggers even though `DrawCardSystem` runs after tasks; bot-org commands must not close.
- `TimeSystem` — while paused, speed change clears pause, sets multiplier, advances time; Unpause **and** speed-resume clear `TutorialOwnsPause`.
- Pause ownership — open while unpaused sets ownership+pause; open while paused does not; complete with ownership unpauses; complete without ownership leaves paused; player resume (space or speed) clears ownership; save/load mid-owned-pause still auto-unpauses on complete.
- `ActiveTaskEntryState` equality includes new fields; accordion helper still collapse-only when expanded; auto-expand behaviour covered by pure helper if extracted (`SelectInitialExpandedTutorial`) matching Refresh previous/current order.
- `SettingsStorage` / `AppPreferences` — default tutorials on; persist completed ids; ClearCompletedTutorials; ResetToDefaults deletes/reloads (+ Unity in-game autosave command).
- Existing part A task / effect / expression / card-draw tests remain green.
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`. Highlight layout/animation and Settings visual polish covered by User Steps.

## Tech Notes

- Prefer extending part A Triggers bag over new ExpressionNode kinds; AND via `mul`.
- Constitution tension (Unity map/UI facts → simulation): presentation **publishes** into a `src/`-consumable bag / commands; `TaskProgressSystem` / `TaskTriggerBag` **consume** — no open/close rules in MonoBehaviours.
- Card-draw close conditions bind to shipped `#153` commands (`DrawCardsCommand`, `ReceiveCardCommand`).
- `DrawCardSystem` order stays after tasks; trigger sampling is what moves earlier.
- Preference progress is app-level (`settings.json` / `gs.preferences.*`), not `game_settings.json`.
- Highlight is UITK-only; animation is presentation-only; active task remains ECS/`ActiveTasks` driven.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material/camera stack changes (MapCameraController only publishes trigger facts).
- **ECS game logic in `src/`** — open/close/force-complete/seed/pause ownership/TimeSystem in `Game.Systems` / components / commands; Unity/Web only publish Triggers and preferences.
- **VContainer sole DI** — register `TutorialPresentationTriggers` / existing `SettingsStorage` via scopes; no static mutable service singletons outside container.
- **UI Toolkit only** — Settings + highlight arrow UXML/USS/Views; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under `Docs/Specs/26_08_08_18_tutorial/` after approved spec.
- **File organisation / assemblies** — core in existing `src` projects; UI in `Assets/Scripts/Unity/UI`; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
