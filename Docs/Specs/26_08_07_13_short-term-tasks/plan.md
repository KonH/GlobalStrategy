# Plan: Short-Term Tasks (Part A)

## Spec

Source: `Docs/Specs/26_08_07_13_short-term-tasks/spec.md` (approved; owner clarifications baked in). Part B (tutorial) is **out of scope**.

**Intent.** Ship short-term tasks that open/complete from configurable `ExpressionNode` conditions for the player org only, optionally apply `EffectConfig` effect IDs and grant resource rewards through the existing income-animation pipeline, and show as an expandable active-only list under the player org HUD panel — with empty checked-in task content.

**Acceptance criteria (summary).**
- Load empty `{ "tasks": [] }` `tasks_config.json` via `IReadOnlyConfigSource<TasksConfig>` on `GameLogicContext` (same pattern as Action/Effect).
- Task schema: required `taskId`, `NameKey`/`DescKey`, `openCondition`/`closeCondition`; optional `reward` (`ActionCost`-shaped), `openEffectIds`/`closeEffectIds` (effect IDs, not action IDs).
- Every tick, player-org only: open → active (apply open effects once); close → completed + rewards/`ResourceChange` + close effects once; completed never reopens; multiple concurrent active allowed; state is `[Savable]`.
- HUD tasks block below player org panel: visible only when ≥1 active task; collapsed name headers; accordion — if details open, any header click only collapses (no switch-to-B); expanded body = description + reward rows.
- Add only `TriggerCondition` expression operand; no tutorial / sample task content.

## Goal

Deliver schema + ECS open/close lifecycle + VisualState projection + UI Toolkit accordion shell against an empty tasks config, reusing ExpressionNode / EffectConfig / ResourceChange patterns so part B can author content without revisiting the runtime spine.

## Approach

### 1. `TasksConfig` + empty JSON + wiring

Add `src/Game.Configs/TasksConfig.cs`:

```csharp
public class TaskRewardEntry { // mirrors ActionCost
	public string ResourceId { get; set; } = ResourceDefinitions.Gold;
	public double Amount { get; set; }
}

public class TaskDefinition {
	public string TaskId { get; set; } = "";
	public string NameKey { get; set; } = "";
	public string DescKey { get; set; } = "";
	public ExpressionNode? OpenCondition { get; set; }
	public ExpressionNode? CloseCondition { get; set; }
	public List<TaskRewardEntry> Reward { get; set; } = new();
	public List<string> OpenEffectIds { get; set; } = new();
	public List<string> CloseEffectIds { get; set; } = new();
}

public class TasksConfig {
	public List<TaskDefinition> Tasks { get; set; } = new();
	public TaskDefinition? Find(string taskId) { /* linear scan like ActionConfig.Find */ }
}
```

Checked-in file: `Assets/Configs/tasks_config.json` → `{ "tasks": [] }` (Newtonsoft case-insensitive maps camelCase JSON to PascalCase POCOs, same as `action_config.json`).

Wire like Action/Effect:
- `GameLogicContext`: optional `IReadOnlyConfigSource<TasksConfig>? tasks = null` + `EmptyTasksConfig` fallback returning `new TasksConfig()`.
- `GameLogic`: load `TasksConfig` in ctor; expose `public TasksConfig TasksConfig { get; }`.
- `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`: `[SerializeField] TextAsset _tasksConfigAsset`; pass `tasks: _tasksConfigAsset != null ? new TextAssetConfig<TasksConfig>(...) : null`.
- `src/Game.ConsoleRunner/Program.cs` `BuildContext`: `FileConfig<TasksConfig>(…/tasks_config.json)`.
- Web client: `IGameConfigSource` + `ConfigProvider` + `GameSession.BuildContext` + `TestGameConfigSource` / `FileGameConfigSource`; add `tasks_config.json` to `CopyConfigsToWwwroot` in `Game.WebClient.csproj` (wwwroot copy is build-generated/gitignored; Assets file is source of truth).
- Optional: thin parity test in `StringConfigParityTests` once the file exists.

**Assumption:** missing/null `openCondition` / `closeCondition` evaluate as **false** (do not call `ExpressionNode.Evaluate(null)`, which returns `1.0`).

### 2. `TriggerCondition` expression hook only

In `src/Game.Configs/ExpressionNode.cs`:
- `ExpressionContext`: add `IReadOnlyDictionary<string, double> Triggers` (default empty) + `GetTrigger(string triggerId)` (missing → `0`; optional throw only if we need parity with relation validation — prefer **0** for absent facts so empty part A stays quiet).
- `ExpressionNode`: add `string TriggerId { get; set; } = ""`; new evaluate case `"triggerCondition"` → `ctx.GetTrigger(node.TriggerId)`.
- Extend `ActionConditionDebug.IsPresentationOperand` / `FormatOperand` so debug formatting recognizes `triggerCondition` (returns `triggerCondition[id] (value)`).

JSON shape for later content:
```json
{ "type": "triggerCondition", "triggerId": "some_fact" }
```

**Part A:** no producer populates `Triggers` in production; tests set dictionary entries directly. Do not add further operand kinds.

### 3. Task condition context (player org)

Add `src/Game.Systems/TaskConditionContext.cs` — builds an `ExpressionContext` for the **player org** without requiring an `ActionDefinition` / card entity:
- Resolve HQ via `_hqCountryByOrgId` (same map `GameLogic` already keeps).
- For HQ country (when present): fill `Control`, `TotalCountryControl`, `IsInWar`, `WarProgress`, `WarFree` (vs HQ, matching the non-revenge branch of `CountryActionConditionContext`), default relation map to zeros / none-candidates as appropriate for no card target.
- Leave opinion / relation-card / revenge fields at `0`/`1` defaults when no card context.
- Attach `Triggers` from the caller (empty in part A production).

**Assumption:** country-scoped operands for tasks are evaluated against the player org’s **HQ country**, so existing expression trees remain usable before part B adds trigger facts.

### 4. Shared effect application (not card play)

Extract org/country effect application from `CreateActionEffectSystem` into a shared helper (e.g. `src/Game.Systems/EffectApplicator.cs`) used by both card play and tasks:

```csharp
public static void ApplyEffectIds(
	World world,
	EffectConfig effectConfig,
	IReadOnlyList<string> effectIds,
	string orgId,
	string countryId,           // "" for task path
	DateTime currentTime,
	/* existing CreateActionEffectSystem deps: rng, settings, topology, centers, maxControlPool, resources, … */
	int contextEntity = -1,     // card entity when present; -1 for tasks
	string correlationId = "",  // actionId or taskId for error messages / ResourceChange EffectId
	string targetRole = "");    // ActionDefinition.TargetRole for cards; "" for tasks (OpinionModifier no-ops)
```

Move the per-`effectDef` branches (`ControlChange`, `OrgResourceGrant`, etc.) into the helper. `CreateActionEffectSystem.Update` keeps scanning `GameAction`+`ActionSucceeded`+`OrgContext`+`CardUse`, then calls the helper with `def.EffectIds`, `uses.CountryId`, and `def.TargetRole`.

**Task path:** call with `countryId: ""`, `contextEntity: -1`, and `targetRole: ""`. Prefer `OrgResourceGrant` for authored task side effects (per spec). Do **not** synthesize `GameAction` / `CardUse`.

When extracting `EffectApplicator`, change the `CountryResourceModifierEffectParams` empty-`countryId` path from **throw** to the same **skip/no-op** guard used by `ControlChange` / `OpinionModifier` / `SetCountryRelation` (cards still always pass a real `CardUse.CountryId`, so fail-fast for mis-wired cards is unchanged in practice). Relation/war branches that also require `RelationCardTarget`/`RevengeCardTarget` on `contextEntity` remain no-ops for tasks (`contextEntity: -1`) and must not call `world.Has` without an entity-validity guard if `countryId` is ever non-empty later.
### 5. ECS runtime: open / close / rewards

**Components** (`src/Game.Components/`, auto-picked up by `SaveSystem` via `[Savable]` reflection):

```csharp
[Savable] public struct TaskId { public string Value; }
[Savable] public struct TaskActive { }
[Savable] public struct TaskCompleted { }
```

One entity per task that has ever activated; completed entities keep `TaskId` + `TaskCompleted` for the save lifetime.

**System** `src/Game.Systems/TaskProgressSystem.cs` (name TBD), called from `GameLogic.Update` **after** relation application/sync (`SetCountryRelationSystem`, `ClearCountryRelationSystem`, and preferably `RelationCardSyncSystem` / `RevengeCardSyncSystem`) and **before** `GameCompletionSystem` / `VisualStateConverter`.

Concrete order (must stay after this tick’s `CleanupActionEffectsSystem` at the start of the tick, and before VisualStateConverter at the end):
`CreateActionEffectSystem` → (optional `SettleCombatResources`) → relation systems → **`TaskProgressSystem.Update(...)`** → … → `GameCompletionSystem` → `VisualStateConverter`.

Rationale: task ExpressionContext reads live relations/control; placing it only “near CreateActionEffectSystem” misses same-tick relation writes. `ResourceChange` from rewards still survives until converter in this slot.

Per tick, resolve player org (`Organization` + `Player`); if none, no-op. Build membership sets from world (`TaskActive` / `TaskCompleted` by `TaskId.Value`). Build one `ExpressionContext` via `TaskConditionContext` (triggers empty).

**Close first (active only):** for each active task whose `CloseCondition` is true:
1. Apply `CloseEffectIds` via `EffectApplicator`.
2. Grant each `Reward` entry: mutate org resource via `ResourceQuery` (same pattern as `OrgResourceGrant` / `AddToExistingResource`) **and** spawn transient `ResourceChange` (`EffectId` like `task_reward_{taskId}_{resourceId}_{ticks}`, `OwnerId` = player org) so `VisualStateConverter.UpdateLastFrameEffects` publishes `VisualResourceChangeEffect`s.

   **Unity animation consumer (required for User Step 4):** `CardPlayAnimator` only holds gold barriers while `_isPlaying`. Add a HUD-side subscriber (e.g. on `HUDDocument` / `PlayerOrgView`) to `LastFrameEffects.PropertyChanged` that, when `CardPlayAnimator` is not playing, for each effect with `OwnerId == player org` and stockpile resources (at least gold), `Hold(-amount)` then `Release(...)` — same pattern as `AnimateGoldDebug` / card-play gold barriers (`animation_barriers.md`: negative offset). Without this, task rewards only snap `Actual` and User Step 4 fails.
3. Remove `TaskActive`; add `TaskCompleted`.
**Then open (inactive, not completed):** for each config task not active/completed whose `OpenCondition` is true:
1. Create entity `TaskId` + `TaskActive`.
2. Apply `OpenEffectIds` once via `EffectApplicator`.

Multiple opens/closes in one tick are independent. **Assumption:** close-before-open prevents same-tick activate+complete.

Skip empty `tasks` list cheaply (no alloc churn).

### 6. VisualState projection

Add to `src/Game.Main/VisualState.cs`:

```csharp
public class ActiveTaskRewardState {
	public string ResourceId { get; }
	public double Amount { get; }
}

public class ActiveTaskEntryState {
	public string TaskId { get; }
	public string NameKey { get; }
	public string DescKey { get; }
	public IReadOnlyList<ActiveTaskRewardState> Rewards { get; }
}

public class ActiveTasksState : INotifyPropertyChanged {
	public IReadOnlyList<ActiveTaskEntryState> Tasks { get; private set; }
	public void Set(List<ActiveTaskEntryState> tasks); // equality-gated
}
```

Wire `public ActiveTasksState ActiveTasks { get; } = new();` on `VisualState`.

`VisualStateConverter`: take `TasksConfig?` (ctor arg from `GameLogic`); each `Update`, scan `TaskActive`+`TaskId`, join config for keys/rewards, `ActiveTasks.Set(...)` (active-only; stable order = config list order among currently active). Add `StateEquality` helpers.

Expanded accordion selection stays **Unity-local** (like Goals org selection / debug deck expand keys) — not in VisualState.

### 7. HUD UI Toolkit block (accordion)

**Layout:** In `Assets/UI/HUD/HUD.uxml`:
1. Add `<ui:Template name="PlayerTasks" src="project://database/Assets/UI/HUD/PlayerTasks/PlayerTasks.uxml"/>` next to the existing `PlayerCountry` template.
2. Import `<ui:Style src="project://database/Assets/UI/HUD/PlayerTasks/PlayerTasks.uss"/>` on the HUD document (required for C#-created header/body class names — see uitoolkit “USS scope for dynamically created elements”).
3. Wrap `player-country` in a column so tasks sit **below** the org panel without disturbing Leaderboard/Goals row siblings:

```xml
<ui:VisualElement name="player-org-column" class="player-org-column" picking-mode="Ignore">
	<ui:Instance template="PlayerCountry" name="player-country" class="player-country-panel"/>
	<ui:Instance template="PlayerTasks" name="player-tasks" class="player-tasks-panel"/>
</ui:VisualElement>
```

USS: `.player-org-column { flex-direction: column; align-items: stretch; min-width: …; }` in `HUD.uss` (`align-items: stretch` needs a defined width per layout gotchas); style `.player-tasks-panel` to match org chrome (padding/border) and hide via view `DisplayStyle.None` when empty.

New template:
- `Assets/UI/HUD/PlayerTasks/PlayerTasks.uxml` — root `player-tasks-root` + container `tasks-list`.
- `Assets/UI/HUD/PlayerTasks/PlayerTasks.uss` — header row / expanded body classes.

Scripts (`GS.Unity.UI`, no new asmdef):
- `PlayerTasksView` — `Refresh(ActiveTasksState)`; hide root when `Tasks.Count == 0`; rebuild/diff list of header elements (localized `NameKey`); expand shows `DescKey` + reward rows (`ResourceConfig` NameKey + amount, format amounts `:F1`). Maintain `_expandedTaskId`.
- Wire headers with `RegisterCallback<PointerUpEvent>` + `e.button == 0 && element.ContainsPoint(e.localPosition)` (same as `GoalsWindowView` / `LensSwitcherView`). Do **not** use `Button.clicked` or `ClickEvent`.
- Accordion rule (extract pure helper for tests): if `_expandedTaskId != null`, any header activation sets it to `null` only; if null, activation sets it to that `taskId`.
- `HUDDocument`: construct view on `player-tasks`; subscribe `ActiveTasks.PropertyChanged` + locale refresh like `PlayerOrgView`; also wire the non-card-play `LastFrameEffects` gold/stockpile Hold/Release subscriber from Approach §5.
Keep tasks **outside** the `player-country` PointerUp that opens OrgInfo so header clicks do not toggle org info.

**Localization:** part A ships **no** task locale entries and **no** chrome keys (list headers are sufficient; empty config → block hidden). Later content uses `localization` skill for `NameKey`/`DescKey`.

### 8. Explicitly out of scope (do not implement)

Tutorial highlight arrow, pause/unpause, settings checkbox, panel tracking, concrete tutorial/gameplay task lists, bot pursuit, completed-task history UI, ActionId-based side effects, new reward VFX.

## Agent Steps

- [ ] **Add TasksConfig + empty JSON** — `TasksConfig.cs`, `Assets/Configs/tasks_config.json` (`{ "tasks": [] }`), wire `GameLogicContext` / `GameLogic` / ConsoleRunner / WebClient `IGameConfigSource`+`ConfigProvider`+`GameSession`+test sources / `Game.WebClient.csproj` copy list / `GameLifetimeScope` TextAsset field.

- [ ] **Add TriggerCondition expression support** — `ExpressionContext.Triggers` + `TriggerId` on `ExpressionNode`, evaluate `"triggerCondition"`, debug format; tests in `ExpressionNodeTests`.

- [ ] **Extract EffectApplicator** — Shared apply path; refactor `CreateActionEffectSystem` to call it; preserve existing card-effect behavior (existing effect tests must stay green).

- [ ] **Task ECS components + TaskProgressSystem + TaskConditionContext** — Savable `TaskId` / `TaskActive` / `TaskCompleted`; open/close/reward/`ResourceChange`; hook into `GameLogic.Update` ordering as above.

- [ ] **ActiveTasks VisualState + converter** — Projection types, equality, `UpdateActiveTasks`, pass `TasksConfig` from `GameLogic`.

- [ ] **PlayerTasks UXML/USS + view + HUD wiring** — Template + parent USS import + column wrap in `HUD.uxml`/`HUD.uss`; `PlayerTasksView` accordion via `PointerUpEvent`+`ContainsPoint`; `HUDDocument` bind/refresh/locale.

- [ ] **Non-card-play resource income animation** — HUD subscriber on `LastFrameEffects` that Hold/Release player-org stockpile deltas when `CardPlayAnimator` is not playing (task rewards and any other non-card `ResourceChange`).

- [ ] **Core tests** — Per Tests section (config, lifecycle, no-repeat, rewards/effects, accordion helper, TriggerCondition).
- [ ] **Validate** — `dotnet test src/GlobalStrategy.Core.sln`; `/dotnet-build Release` after `src/` changes.

## User Steps

These steps require Unity Editor scene/asset work, visual inspection in the Editor, or other hands-on Unity steps the agent cannot perform.

### 1. Assign tasks_config TextAsset on GameLifetimeScope

Open `Assets/Scenes/Map.unity`, select `GameLifetimeScope`, and assign `Assets/Configs/tasks_config.json` to the new `_tasksConfigAsset` field (same pattern as `_actionConfigAsset` / `_effectConfigAsset`). Confirm Play mode loads with no missing-config / null-root errors.

### 2. HUD layout under player org

Enter Play mode with a normal session: confirm the tasks block sits directly under the player org panel (not beside Leaderboard/Goals), and with empty config the block is fully hidden (no empty shell).

### 3. Accordion interaction (dev fixture)

Temporarily author one or two tasks in `tasks_config.json` (or use a debug override if implement adds one) so the list is visible. Confirm: click header A expands description + rewards; with A open, clicking A or B only collapses; a second click on B then expands B. Revert temporary content before merge if it is not meant to ship (checked-in file must remain `{ "tasks": [] }`).

### 4. Income animation on complete

With a temporary task that grants a gold reward on close, complete it and confirm the existing player-org gold income animation plays (no new VFX). Remove temporary content afterward.

## Tests

- `TasksConfig` deserialize — empty file; full sample JSON with conditions / rewards / effect id lists; missing optionals → empty lists.
- `ExpressionNodeTests` — `triggerCondition` reads context Triggers; missing id → 0; composes with `gte`.
- `TaskProgressSystemTests` (new) — open when condition true; no re-open when active/completed; close applies completed marker; concurrent actives; close-before-open same tick; completed survives simulated save membership (component round-trip or `SaveLoadRoundTripTests` extension with Task* components).
- Effects/rewards — open/close call `EffectApplicator` for `OrgResourceGrant`; reward creates matching `Resource` delta + `ResourceChange`; country-targeted effect ids with empty country **no-op without throwing** (including `CountryResourceModifier` after the throw→skip change).
- `TaskAccordionInteraction` (or equivalent pure helper) — collapse-only when expanded; expand when none expanded.
- Existing `CreateActionEffectSystem` / sell-arms / declare-war / opinion effect tests remain green after extraction (`targetRole` passed from card path).- Full suite: `dotnet test src/GlobalStrategy.Core.sln`. UI layout/pointer covered by User Steps.

## Tech Notes

- Config precedent: `ActionConfig` / `EffectConfig` + `TextAssetConfig` / `FileConfig` / WebClient copy target.
- Expression precedent: `ExpressionNode` + `CountryActionConditionContext`; tasks use slim `TaskConditionContext` + Triggers.
- Effect precedent: `CreateActionEffectSystem` OrgResourceGrant + `ResourceChange`; cleanup timing via `CleanupActionEffectsSystem` at next tick start.
- Save precedent: `[Savable]` on `Game.Components` auto-registered by `SaveSystem`.
- UI precedent: `PlayerOrgView` + Goals/Leaderboard VisualState binding; accordion click rule from spec (not debug multi-expand).
- Do not ship tutorial part B or non-empty tasks content.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material/camera changes.
- **ECS game logic in `src/`** — open/close/rewards/effects in `Game.Systems` / components; Unity Document/View only present and handle accordion input.
- **VContainer sole DI** — extend existing `GameLifetimeScope` TextAsset + `GameLogicContext`; HUD binds via injected `VisualState`.
- **UI Toolkit only** — UXML/USS + View; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under `Docs/Specs/26_08_07_13_short-term-tasks/` after approved spec; part B deferred.
- **File organisation / assemblies** — core in existing `src` projects; UI in `Assets/Scripts/Unity/UI`; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

## Open questions for owner

None — locked defaults from codebase precedent are noted as **Assumption** under Approach §§1, 3, and 5.

Use the implement skill to start working on the plan or request changes.
