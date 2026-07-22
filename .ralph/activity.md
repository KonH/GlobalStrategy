# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-22 — Add completion-condition configuration and default objective

Task: "Add the recursive completion-condition configuration model and configured default objective."

**What I changed:**
- Added `src/Game.Configs/CompletionConditionConfig.cs` with independent `Type`, numeric
  `Value`, and recursive `Members` fields for world-level completion conditions.
- Added `GameSettings.CompletionCondition` with the backward-compatible default `any` tree:
  `total_control` at `0.8` or `full_control_countries` at `15`.
- Added the same explicit camelCase objective tree to `Assets/Configs/game_settings.json`.
- Kept completion configuration separate from the action-specific `ExpressionContext`.
- Refreshed the tracked Unity-consumed Release assemblies under `Assets/Plugins/Core`.

**Gate:** `dotnet build src/GlobalStrategy.Core.sln -c Release` exited 0. Evidence:
`Build succeeded.`, 0 warnings, 0 errors; all solution projects built, including
`Game.Configs.dll`, in 14.04 seconds.

The next iteration should implement the completion-condition contract, leaf conditions,
recursive factory, shared filtered control aggregation, and validation.

---

## 2026-07-22 — Implement completion-condition evaluation and validation

Task: "Implement the completion-condition contract, leaves, recursive composition, and validation."

**What I changed:**
- Added `ICompletionCondition` and `CompletionConditionContext`, including an ordinal
  available-country set and positive control-capacity validation.
- Added recursive `AnyCompletionCondition`, inclusive `TotalControlCondition`, and inclusive
  `FullControlCondition` evaluators under `src/Game.Systems/`.
- Added `CompletionConditionFactory` with contextual validation for null/unknown nodes, empty
  `any` groups, invalid thresholds, and non-positive control capacity.
- Extended `OrgMetrics.GetControlByCountry` with a shared available-country-filtered aggregation
  overload that excludes other organizations/unavailable countries and sums repeated contributions.
- Refreshed the tracked Unity-consumed Release assemblies under `Assets/Plugins/Core` and bumped
  `ProjectSettings.asset` bundle version from `1.41` to `1.42` for the commit.

**Gate:** `dotnet build src/GlobalStrategy.Core.sln -c Release` exited 0. Evidence:
`Build succeeded.`, 0 warnings, 0 errors; all solution projects built, including the updated
`Game.Systems.dll`, in 19.37 seconds.

The next iteration should add the savable ECS completion singleton and per-organization outcome
components. Condition behavior has compilation coverage here; focused regression tests remain in
the later `completion-condition-tests` task.

---

## 2026-07-22 — Add savable completion and organization outcome components

Task: "Add savable ECS completion and per-organization outcome components."

**What I changed:**
- Added `src/Game.Components/GameCompletion.cs` with the savable terminal flag and winner
  organization ID.
- Added `src/Game.Components/OrganizationGameOutcome.cs` with the savable participation order,
  `OrganizationGameResult`, and `InProgress`, `Winner`, and `Loser` values.
- Kept organization outcomes shaped for direct attachment to existing organization entities.
- Refreshed the tracked Unity-consumed Release assemblies under `Assets/Plugins/Core` and bumped
  `ProjectSettings.asset` bundle version from `1.42` to `1.43` for the commit.

**Gate:** `dotnet build src/GlobalStrategy.Core.sln -c Release` exited 0. Evidence:
`Build succeeded.`, 0 warnings, 0 errors; all solution projects built, including the updated
`Game.Components.dll`, in 7.13 seconds.

The next iteration should initialize exactly one in-progress completion singleton and attach
ordered in-progress outcomes to every resolved participant in `InitSystem`.

---

## 2026-07-22 — Initialize completion state and participant outcomes

Task: "Initialize the completion singleton and ordered participant outcomes."

**What I changed:**
- Updated `src/Game.Main/InitSystem.cs` to create exactly one in-progress `GameCompletion`
  singleton during first-time world initialization.
- Attached an in-progress `OrganizationGameOutcome` directly to each organization entity,
  using its index in `ResolveParticipatingOrgs` as the stable `ParticipationOrder`.
- Added focused initialization regression coverage in `src/Game.Tests/InitSystemTests.cs`.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.43` to `1.44`.

**Gate:** `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test src/GlobalStrategy.Core.sln`
exited 0. The runner has .NET 10 but not the targeted .NET 8 runtime, so the standard gate's
first invocation built successfully but its test hosts could not launch; major roll-forward
allowed the same built test suite to execute. Evidence: ECS.Tests passed 34/34,
ECS.Viewer.Tests passed 16/16, and Game.Tests passed 335/335; 385 total tests, 0 failed.

The next iteration should implement deterministic, idempotent winner selection and outcome
assignment in `GameCompletionSystem`.

---

## 2026-07-22 — Implement deterministic winner selection and outcome assignment

Task: "Implement deterministic, idempotent winner selection and outcome assignment."

**What I changed:**
- Added `src/Game.Systems/GameCompletionSystem.cs` with an early return for an already completed
  singleton and safe no-op behavior when there are no ECS countries or no participants.
- Collected participants from organization entities carrying `OrganizationGameOutcome`, sorted them
  by persisted `ParticipationOrder`, and used ordinal organization ID as the deterministic fallback
  for duplicate malformed orders.
- Evaluated the configured condition against the ECS country set, selected only the first qualifier,
  assigned that entity `Winner` and every other participant `Loser`, then sealed the completion
  singleton so repeated evaluation preserves the terminal result.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.44` to `1.45`.

**Gate:** `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test src/GlobalStrategy.Core.sln`
exited 0. The exact gate first compiled successfully but could not launch its net8.0 test hosts
because this runner has .NET 10 rather than .NET 8; major roll-forward ran those same built test
assemblies. Evidence: ECS.Tests passed 34/34, ECS.Viewer.Tests passed 16/16, and Game.Tests passed
335/335; 385 total tests, 0 failed.

The next iteration should expose the player-facing completion result through `VisualState` and
project it relative to the current player in `VisualStateConverter`. `GameCompletionSystem` is not
wired into `GameLogic` yet; that remains the later completion-orchestration task.

---

## 2026-07-22 — Expose the player-facing completion result through VisualState

Task: "Expose the player-facing completion result through VisualState."

**What I changed:**
- Added `GameResult` values `InProgress`, `Win`, and `Lose`, plus observable
  `GameCompletionState` fields for completion, winner organization ID, and player-relative result.
- Added `GameCompletion` to `VisualState` and updated `VisualStateConverter` to project the ECS
  completion singleton relative to the resolved player organization without evaluating game rules.
- Kept missing or incomplete completion state projected as `InProgress` for legacy/pre-reconciliation
  worlds, and refreshed the tracked Unity-consumed Release assemblies under `Assets/Plugins/Core`.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.45` to `1.46`.

**Gate:** `dotnet build src/GlobalStrategy.Core.sln -c Release` exited 0. Evidence:
`Build succeeded.`, 0 warnings, 0 errors; all solution projects built, including the updated
`Game.Main.dll`, in 18.94 seconds.

The next iteration should wire the configured completion condition and singleton into `GameLogic`,
evaluate after all winning-tick mutations, publish the final state, freeze later gameplay, and guard
bot behavior after terminal completion.

---

## 2026-07-22 — Wire completion orchestration and terminal bot guards

Task: "Wire completion evaluation, final publication, terminal freezing, and bot guards into the game loop."

**What I changed:**
- Updated `src/Game.Main/GameLogic.cs` to build and cache the configured completion condition, cache
  the completion singleton, and expose `IsCompleted`.
- Evaluated completion after every tick mutation and before command clearing/final visual-state
  publication, preserving the complete winning tick.
- Added a terminal update branch that processes pending save commands, discards all commands, and
  skips simulation, animation ticks, and visual republication.
- Made direct bot-action logging a no-op after completion and updated `src/Game.Bots/BotSession.cs`
  to skip terminal decision ticks while continuing to call `GameLogic.Update`.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.46` to `1.47`.

**Gate:** The exact `dotnet test src/GlobalStrategy.Core.sln` invocation compiled successfully but
could not launch net8.0 test hosts because this runner has .NET 10 rather than .NET 8. Re-running the
same suite with `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test src/GlobalStrategy.Core.sln` exited 0.
Evidence: ECS.Tests passed 34/34, ECS.Viewer.Tests passed 16/16, and Game.Tests passed 335/335;
385 total tests, 0 failed.

The next iteration should reconcile completion state, participant order, and immediate projection
when loading snapshots. Focused completion orchestration regression coverage remains in the later
`completion-system-tests` task.

---

## 2026-07-22 — Reconcile completion state when loading snapshots

Task: "Reconcile completion state, participant order, and immediate projection when loading snapshots."

**What I changed:**
- Updated `src/Game.Main/GameLogic.cs` to clear pre-load commands, refresh loaded singleton IDs,
  restore `_previousTime` from the loaded `GameTime`, evaluate completion once, and immediately
  project the loaded state with zero delta.
- Added legacy-load reconciliation that creates a missing in-progress `GameCompletion`, preserves
  valid saved outcome orders, and reconstructs missing outcomes from configured participant order,
  the initial-player fallback, then unmatched loaded organizations in ordinal order.
- Added fail-fast validation for duplicate loaded organization IDs, negative participation orders,
  and duplicate saved participation orders.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.47` to `1.48`.

**Gate:** `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test src/GlobalStrategy.Core.sln` exited 0.
Evidence: ECS.Tests passed 34/34, ECS.Viewer.Tests passed 16/16, and Game.Tests passed 335/335;
385 total tests, 0 failed. Major roll-forward was required because this runner provides the .NET 10
runtime rather than the targeted .NET 8 runtime.

The next iteration should add configuration and completion-condition tree regression coverage.
Focused legacy/terminal persistence cases remain in the later `completion-persistence-tests` task.

---

## 2026-07-22 — Add completion-condition configuration and tree regression coverage

Task: "Add configuration and condition-tree regression coverage."

**What I changed:**
- Added `src/Game.Tests/CompletionConditionTests.cs` covering recursive `any` composition,
  configured threshold changes, inclusive total-control and full-country boundaries, and
  below-threshold behavior.
- Covered repeated same-country contributions, other-organization and unavailable-country
  exclusion, zero-control countries in total capacity, and zero-country safety.
- Covered camelCase recursive trees and absent-key defaults through both `FileConfig` and
  Newtonsoft, plus contextual errors for null/unknown trees, empty groups, invalid thresholds,
  and non-positive capacity.
- Updated `src/Game.Configs/CompletionConditionConfig.cs` so Newtonsoft replaces initialized
  recursive member lists instead of appending JSON members to them.
- Bumped `ProjectSettings/ProjectSettings.asset` bundle version from `1.48` to `1.49`.

**Gate:** `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test src/GlobalStrategy.Core.sln` exited 0.
Evidence: ECS.Tests passed 34/34, ECS.Viewer.Tests passed 16/16, and Game.Tests passed 349/349;
399 total tests, 0 failed. Major roll-forward was required because this runner provides the .NET 10
runtime rather than the targeted .NET 8 runtime.

The next iteration should add winner-selection and game-loop completion integration coverage.

---
