# Spec: Short-Term Tasks

Connected pair: this folder is **part A** of issue #143 (short-term tasks). Part B (tutorial — highlight arrow, pause/unpause, settings checkbox, panel/window tracking, and the concrete tutorial task list) is a **later connected spec** that builds on the tasks concept; it is out of scope here.

## Feature Intent

As a player, I want short-term tasks that become available and complete based on configurable conditions, optionally run open/close actions and grant resource rewards, and show as an expandable list under my organization panel, so that I have clear, guided objectives during a session without waiting for the tutorial layer.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Config

- The game loads configs at startup / session start.
  - A new `tasks_config.json` is present under the project's usual config load path (`Assets/Configs/`) and is wired into `GameLogicContext` the same way other `IReadOnlyConfigSource<T>` configs are (e.g. `Action`, `Effect`).
  - The file exposes a list of available task definitions. Each definition includes at least: a stable task identity, `name`, `description`, `openCondition`, `closeCondition`, optional `reward` (resource list), and optional `openActions` / `closeActions` (lists of action IDs).
  - Missing optional fields (`reward`, `openActions`, `closeActions`) are treated as empty / absent — activation and completion still work without them.
  - [NEEDS CLARIFICATION for initial content and exact JSON field names / condition & reward schemas — see Ambiguities.]

### Runtime — open / activate

- A task is neither active nor completed, and its `openCondition` evaluates true for the relevant scope this tick.
  - The runtime marks that task active (ECS marker / state component on a task-tracking entity, not Unity MonoBehaviour state).
  - If the task defines `openActions`, those actions are executed once at activation (semantics of "execute" are open — see Ambiguities).
  - If `openCondition` is false => the task stays inactive and no `openActions` run.
- Multiple tasks may satisfy `openCondition` in the same tick or across ticks.
  - Each eligible non-active, non-completed task can become active independently — more than one task may be active at once (the UI lists multiple headers).
- A task is already active or already completed.
  - Its `openCondition` is not re-applied to re-activate it; completed tasks never re-enter the available/active set from open checks alone.

### Runtime — close / complete

- A task is active and its `closeCondition` evaluates true this tick.
  - The runtime marks that task completed (ECS marker / state component).
  - If the task defines `closeActions`, those actions are executed once at completion (same open semantic ambiguity as `openActions`).
  - If the task defines a non-empty `reward`, each listed resource amount is granted to the player organization through the existing resource-change pipeline so the HUD plays the existing income animation (`ResourceChange` → `VisualStateConverter` → `VisualResourceChangeEffect`), not a new reward VFX.
  - If `closeCondition` is false => the task stays active; no completion side effects run.
- A task has been completed once.
  - It is recorded in the completed-task set and is not opened or completed again for the rest of that save's lifetime (no repeat).
  - Completed-task membership is `[Savable]` runtime state so it survives save/load (not recomputable from config alone).

### UI — visibility and list

- The HUD is visible and the player organization panel is shown.
  - A tasks block sits **below** the player org panel (`Assets/UI/HUD/PlayerCountry/PlayerCountry.uxml` instance `player-country` in `HUD.uxml`, wired via `PlayerOrgView` / `HUDDocument`).
- At least one non-finished (active, not completed) task exists.
  - The tasks block is visible and shows a list of collapsed task headers (task names only).
- Zero non-finished tasks exist (none active, or all previously active ones completed).
  - The tasks block is hidden (no empty shell).

### UI — expand / collapse

- The tasks block is visible with one or more collapsed headers, and no task details are currently expanded.
  - Player clicks a collapsed task header => that item expands to show task details (at least the description; reward/actions presentation is not required beyond what is needed to convey the task), and other list items remain visible.
- Task details are currently shown for an expanded item.
  - Player clicks that same item's header (collapsed or expanded affordance as implemented) => that item's details close and the item collapses.
  - [NEEDS CLARIFICATION: whether clicking a *different* collapsed header while details are open switches expansion to that item, or only the currently expanded header closes details — see Ambiguities.]

## Tech Notes

- **Placement:** Extend or sibling the player-org HUD region under `player-country` / immediately below `.player-country-panel` in `Assets/UI/HUD/HUD.uxml`. Presentation glue stays in Unity UI Toolkit + View classes (`PlayerOrgView`-adjacent or a dedicated tasks view); all open/close/reward logic lives in `src/` ECS systems per Constitution.
- **Conditions precedent:** Action playability and event-notification settings already use `ExpressionNode` trees (`src/Game.Configs/ExpressionNode.cs`, evaluated via `ExpressionNode.Evaluate` against `ExpressionContext`). That is the strongest existing candidate for `openCondition` / `closeCondition`, but the ExpressionContext surface today is card/relation/war-oriented — task conditions may need new operands or a different expression shape (flagged in Ambiguities).
- **Actions precedent:** Actions live in `action_config.json` / `ActionDefinition` keyed by `ActionId`, with `NameKey` / `DescKey` locale keys, `Conditions`, `Cost`, and `EffectIds`. There is **no** standalone "run ActionId for org" API outside the card-play pipeline (`CheckActionConditionSystem` → cost → `ActionSucceeded` → `CreateActionEffectSystem`, which requires `GameAction` + `ActionSucceeded` + `OrgContext` + `CardUse`). `openActions` / `closeActions` therefore need an explicit owner decision on semantics (fire effect IDs only? spawn a synthetic succeeded card play? ignore cost/conditions?).
- **Rewards / income animation:** Grant by creating transient `ResourceChange` components (`src/Game.Components/ResourceChangeEffect.cs`: `EffectId`, `ResourceId`, `OwnerId`, `Amount`) scanned by `VisualStateConverter` into `VisualResourceChangeEffect`. That is what "just income animation for now" maps to — reuse the pipeline, do not invent new VFX. Actual resource balance updates must also apply through the normal resource systems (same as other grants), not animation-only.
- **ECS state:** Prefer durable `[Savable]` state components for active/completed membership (and any per-task entity identity), analogous to other runtime progress (`ActionCooldownState`, `CardInHand`, etc.). One-shot markers (e.g. `ForceResourceRecompute`) are a pattern for same-tick signals, not for long-lived active/completed sets.
- **Localization precedent:** Player-facing action strings use locale keys (`NameKey` / `DescKey` on `ActionDefinition`). Task `name` / `description` should follow the same key-vs-raw decision (Ambiguities); whatever is chosen, new keys go through `Assets/Localization/en.asset` + `ru.asset` via the localization skill at implement time.
- **Config wiring:** Add `IReadOnlyConfigSource<TasksConfig>` (name TBD) on `GameLogicContext` and load from `Assets/Configs/tasks_config.json`, mirroring `Action` / `Effect` sources.
- **No concrete task list in this spec:** Owner deferred tutorial task content to part B. Part A ships schema + runtime/UI; sample entries only if the owner chooses them in Ambiguities — do not invent a first content pack here.

## Out of Scope

- **Tutorial (part B of #143)** — entirely deferred to a later connected spec, including:
  - Highlight / pointing arrow with back-and-forth animation tied to a task
  - Pause / unpause driven by tutorial tasks
  - Main menu → Settings → Tutorials checkbox (default enabled)
  - Tracking which panels / windows are open as tutorial progress signals
  - Any concrete tutorial task list or task IDs for onboarding
- Designing or shipping a starter set of gameplay tasks (content pack) beyond an empty/skeleton config unless the owner resolves Ambiguity 0 in favor of samples.
- New reward presentation beyond the existing resource income animation (toasts, modal "quest complete", particles, sound).
- Task UI outside the HUD block below the player org panel (no dedicated Tasks window, no Goals-window integration, no map markers).
- Bot / AI consumption of tasks (bots do not need to "pursue" short-term tasks in this slice).
- Multiplayer sync of task state.
- Re-opening completed tasks, daily resets, or failure/expiry states other than open → active → completed.
- Changing the card-play pipeline itself except insofar as plan-phase work must hook or reuse it for `openActions` / `closeActions` once semantics are decided.

## Ambiguities

- 0. [NEEDS CLARIFICATION: Initial `tasks_config.json` content] Should the checked-in file be an empty skeleton (`{ "tasks": [] }` or equivalent schema-only), or include a small set of sample/debug tasks? Owner said the tutorial list comes in part B — prefer empty/schema-only unless you want non-tutorial samples for A.
- 1. [NEEDS CLARIFICATION: Condition format] Should `openCondition` / `closeCondition` reuse `ExpressionNode` trees (as in `action_config.json` conditions and `EventNotificationSettings`), possibly with new `ExpressionContext` operands for task-relevant facts, or a different shape (simple predicate IDs, script hooks, etc.)?
- 2. [NEEDS CLARIFICATION: `openActions` / `closeActions` semantics] Action IDs today only run through the card-play pipeline (`GameAction` + `CardUse` + conditions + cost + `CreateActionEffectSystem`). When a task lists action IDs, should the runtime (a) fire that action's `EffectIds` for the player org as if the card succeeded (skipping cost/hand/conditions), (b) synthesize a full card-play (respecting cost/conditions), (c) mean something else (e.g. spawn cards into hand), or (d) defer action lists until a follow-up once a "execute ActionId" path exists?
- 3. [NEEDS CLARIFICATION: Accordion UX when details are already open] If task A's details are expanded and the player clicks collapsed task B's header, should the UI (a) switch expansion to B (collapse A, expand B), or (b) only close details when clicking the currently expanded header (clicking B does nothing until A is collapsed)? Issue text only specifies open-when-none and close-when-details-shown.
- 4. [NEEDS CLARIFICATION: `name` / `description` localization] Are these locale keys (preferred by action-card precedent: `NameKey` / `DescKey`) or raw display strings in the config? If keys, what naming prefix (e.g. `task.<id>.name`)?
- 5. [NEEDS CLARIFICATION: Evaluation cadence and scope] Should open/close conditions be evaluated every simulation tick for the player organization only, or on a coarser cadence / for every organization? Issue UI is player-org-panel-centric — confirm player-org-only runtime.
- 6. [NEEDS CLARIFICATION: Concurrent active tasks] Spec assumes multiple tasks may be active simultaneously (list UI). Confirm that is intended, vs. at most one active task at a time.
- 7. [NEEDS CLARIFICATION: Reward resource list schema] Should each reward entry mirror `ActionCost` (`{ "resourceId": "gold", "amount": N }`), or another shape? Are amounts always positive grants to the player org's stockpile resources only (gold/recruits/etc.), or can they target other owners/resource kinds?
- 8. [NEEDS CLARIFICATION: Task identity field] Config description listed name/description/conditions/rewards/actions but not an explicit id. Should each task have a stable `taskId` string (required for save keys, completed-set membership, and part B tutorial binding), with `name`/`description` as separate display fields/keys?
- 9. [NEEDS CLARIFICATION: Task details contents] On expand, is description-only enough for A, or must the expanded body also show reward summary and/or other metadata?
