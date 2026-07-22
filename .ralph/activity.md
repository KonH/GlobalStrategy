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
