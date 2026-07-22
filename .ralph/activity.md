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
