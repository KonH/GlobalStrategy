# Spec: Short-Term Tasks

Connected pair: this folder is **part A** of issue #143 (short-term tasks). Part B (tutorial — highlight arrow, pause/unpause, settings checkbox, panel/window tracking, and the concrete tutorial task list) is a **later connected spec** that builds on the tasks concept; it is out of scope here.

## Feature Intent

As a player, I want short-term tasks that become available and complete based on configurable conditions, optionally apply open/close effects and grant resource rewards, and show as an expandable list under my organization panel, so that I have clear, guided objectives during a session without waiting for the tutorial layer.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Config

- The game loads configs at startup / session start.
  - A new `tasks_config.json` is present under `Assets/Configs/` and is wired into `GameLogicContext` the same way other `IReadOnlyConfigSource<T>` configs are (e.g. `Action`, `Effect`).
  - The checked-in file is empty / schema-only: `{ "tasks": [] }` (or equivalent empty `tasks` list). No sample or tutorial task content is shipped in part A.
  - Each future task definition includes at least:
    - required stable `taskId` (string) — save keys, completed-set membership, and later part B binding
    - `NameKey` / `DescKey` — locale keys (same style as `ActionDefinition.NameKey` / `DescKey`), not raw display strings
    - `openCondition` / `closeCondition` — `ExpressionNode` trees
    - optional `reward` — list of `{ "resourceId": "...", "amount": N }` entries mirroring `ActionCost`
    - optional `openEffectIds` / `closeEffectIds` — lists of effect IDs from `EffectConfig` (**not** action IDs)
  - Missing optional fields (`reward`, `openEffectIds`, `closeEffectIds`) are treated as empty / absent — activation and completion still work without them.

### Runtime — evaluation scope

- Every simulation tick, for the **player organization only**.
  - Open and close conditions are evaluated against an `ExpressionContext` populated for that player org (not for every org, not on a coarser cadence).

### Runtime — open / activate

- A task is neither active nor completed, and its `openCondition` evaluates true for the player org this tick.
  - The runtime marks that task active (ECS marker / state component on a task-tracking entity, not Unity MonoBehaviour state).
  - If the task defines non-empty `openEffectIds`, those effects are applied once at activation for the player org via an extended effect-application path (see Tech Notes) — not by synthesizing a card play and not by resolving ActionIds.
  - If `openCondition` is false => the task stays inactive and no `openEffectIds` run.
- Multiple tasks may satisfy `openCondition` in the same tick or across ticks.
  - Each eligible non-active, non-completed task can become active independently — **multiple concurrent active tasks** are allowed.
- A task is already active or already completed.
  - Its `openCondition` is not re-applied to re-activate it; completed tasks never re-enter the available/active set from open checks alone.

### Runtime — close / complete

- A task is active and its `closeCondition` evaluates true this tick.
  - The runtime marks that task completed (ECS marker / state component).
  - If the task defines non-empty `closeEffectIds`, those effects are applied once at completion for the player org (same effect-application path as open).
  - If the task defines a non-empty `reward`, each listed `{ resourceId, amount }` entry is granted to the **player organization** stockpile through the existing resource-change pipeline so the HUD plays the existing income animation (`ResourceChange` → `VisualStateConverter` → `VisualResourceChangeEffect`), not a new reward VFX.
  - If `closeCondition` is false => the task stays active; no completion side effects run.
- A task has been completed once.
  - It is recorded in the completed-task set and is not opened or completed again for the rest of that save's lifetime (no repeat).
  - Completed-task membership is `[Savable]` runtime state so it survives save/load (not recomputable from config alone).

### UI — visibility and list

- The HUD is visible and the player organization panel is shown.
  - A tasks block sits **below** the player org panel (`Assets/UI/HUD/PlayerCountry/PlayerCountry.uxml` instance `player-country` in `HUD.uxml`, wired via `PlayerOrgView` / `HUDDocument`).
- At least one active (non-completed) task exists.
  - The tasks block is visible and shows a list of collapsed task headers (**names only**, resolved from each task's `NameKey`).
  - The list shows **only active** tasks — completed tasks do not appear.
- Zero active tasks exist (none active, or all previously active ones completed).
  - The tasks block is hidden (no empty shell).

### UI — expand / collapse (accordion)

- The tasks block is visible with one or more collapsed headers, and **no** task details are currently expanded.
  - Player clicks a collapsed task header => that item expands to show task details: **description** (from `DescKey`) **and rewards** (summary of the reward list; empty/absent reward => no reward rows). Other list items remain visible and collapsed.
- Task details are currently shown for an expanded item (A).
  - Player clicks **any** task header (A's header or another collapsed header B) => **only** the currently expanded item closes (collapse A). Do **not** expand B (or any other item) in the same click. Expanding another item requires a subsequent click when no details are open.

## Tech Notes

- **Placement:** Extend or sibling the player-org HUD region under `player-country` / immediately below `.player-country-panel` in `Assets/UI/HUD/HUD.uxml`. Presentation glue stays in Unity UI Toolkit + View classes (`PlayerOrgView`-adjacent or a dedicated tasks view); all open/close/reward/effect logic lives in `src/` ECS systems per Constitution (ECS for game logic; UI Toolkit only for presentation).
- **Conditions:** Reuse `ExpressionNode` trees (`src/Game.Configs/ExpressionNode.cs`, evaluated via `ExpressionNode.Evaluate` against `ExpressionContext`) for `openCondition` / `closeCondition`, same shape as action playability conditions. **Add only one new expression kind / `ExpressionContext` operand for this feature: `TriggerCondition`** — for task-relevant trigger facts. Existing operands (`control`, `opinion`, `warFree`, etc.) remain available; do not add further new operand kinds in part A beyond `TriggerCondition`. Concrete authored trigger facts and task content arrive with later content / part B; part A ships the evaluation hook and empty config.
- **Effects (not actions):** Schema uses `openEffectIds` / `closeEffectIds` (lists of `EffectConfig` effect IDs). Do **not** use action IDs for open/close side effects. Today's `CreateActionEffectSystem` only runs inside the card-play pipeline (`GameAction` + `ActionSucceeded` + `OrgContext` + `CardUse`). Part A **extends the effect concept** so listed effect IDs can be applied for the player org at task open/close without synthesizing a card play (shared helper or task-driven apply path that resolves `EffectConfig.Find` and applies the matching effect params). Country-targeted effects that need a `CardUse.CountryId` are out of band for empty config; plan should prefer org-scoped effect types (e.g. `OrgResourceGrant`) for task side effects unless a clear player-org target exists.
- **Rewards / income animation:** Reward entries mirror `ActionCost` (`ResourceId` + `Amount`). Grant by updating player-org resources through the normal resource systems **and** creating transient `ResourceChange` components (`src/Game.Components/ResourceChangeEffect.cs`: `EffectId`, `ResourceId`, `OwnerId`, `Amount`) scanned by `VisualStateConverter` into `VisualResourceChangeEffect`. Reuse that pipeline; do not invent new VFX.
- **ECS state:** Prefer durable `[Savable]` state components for active/completed membership keyed by `taskId` (and any per-task entity identity), analogous to other runtime progress (`ActionCooldownState`, `CardInHand`, etc.). One-shot markers (e.g. `ForceResourceRecompute`) are for same-tick signals, not for long-lived active/completed sets.
- **Localization:** Task display strings use `NameKey` / `DescKey` on the task definition (precedent: `ActionDefinition` in `src/Game.Configs/ActionConfig.cs`). New keys go through `Assets/Localization/en.asset` + `ru.asset` via the localization skill when content is added; part A's empty `tasks` list needs no locale entries yet.
- **Config wiring:** Add `IReadOnlyConfigSource<TasksConfig>` (name TBD) on `GameLogicContext` and load from `Assets/Configs/tasks_config.json`, mirroring `Action` / `Effect` sources. Checked-in content: empty `tasks` array only.
- **No concrete task list in this spec:** Tutorial and gameplay task content belong to part B / later content work. Part A is schema + runtime + UI shell against an empty config.

## Out of Scope

- **Tutorial (part B of #143)** — entirely deferred to a later connected spec, including:
  - Highlight / pointing arrow with back-and-forth animation tied to a task
  - Pause / unpause driven by tutorial tasks
  - Main menu → Settings → Tutorials checkbox (default enabled)
  - Tracking which panels / windows are open as tutorial progress signals
  - Any concrete tutorial task list or task IDs for onboarding
- Designing or shipping a starter set of gameplay tasks (content pack); checked-in config stays empty.
- New reward presentation beyond the existing resource income animation (toasts, modal "quest complete", particles, sound).
- Task UI outside the HUD block below the player org panel (no dedicated Tasks window, no Goals-window integration, no map markers).
- Bot / AI consumption of tasks (bots do not need to "pursue" short-term tasks in this slice).
- Multiplayer sync of task state.
- Re-opening completed tasks, daily resets, or failure/expiry states other than open → active → completed.
- Using ActionIds for open/close side effects, or changing the card-play pipeline solely to run task side effects (extend effect application instead).
- Showing completed tasks in the HUD list (list is active-only).
