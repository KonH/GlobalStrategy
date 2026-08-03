# Spec: Score Count Goal

## Feature Intent

As a player, I want a new victory condition that is met when my organization's score reaches a goal value derived from the shipped end-game comparison scores, so that a purely score-driven campaign has its own path to winning instead of requiring territorial control thresholds.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The shipped end-game comparison scores are prepared for this feature.
  - The nine predefined comparison scores shown in the end-game comparison block are read => the feature ships => every one of them is 20% lower than its current value, and their relative order (lowest to highest) is unchanged.
  - The decreased comparison scores are shipped => the end-game window is opened after any game ends => the comparison block still shows the player row correctly ranked among the nine predefined entries at their new, lower values.
- The score-count goal value is established from the decreased comparison scores.
  - The nine decreased comparison scores are known => the goal value is derived => it equals the new highest comparison score plus 100.
  - The organization-selection screen's win-conditions panel is opened => the score-count goal is a configured objective => a row describes the score goal so the player knows the numeric target before starting.
- A game is in progress with the score-count goal included in the configured completion objective.
  - No participating organization's score has reached the goal value => the game finishes an update => the game remains in progress, no winner or loser is assigned, and normal updates continue.
  - One participating organization's score reaches at least the goal value => the game finishes the update that reached the threshold => that organization is the sole winner and every other participating organization is a loser, the same way an existing control-based win is resolved.
  - A participating organization's score reaches exactly the goal value => the game evaluates the objective => equality counts as meeting the threshold.
  - More than one participating organization's score meets the goal in the same update => the game selects the winner => exactly one winner is chosen in the same stable deterministic order already used for the existing control-based conditions.
- The game completes by reaching the score-count goal.
  - The player organization is the winner => the end-game window appears => it presents the same win presentation, leaderboard, and comparison block already used for a control-based win, with no separate "reason for victory" messaging required.
  - Another organization is the winner by score => the end-game window appears => it presents the same lose presentation already used for a control-based loss.

## Tech Notes

- Decreasing the nine shipped comparison scores by 20%:
  - Apply a flat `× 0.8` multiplier to the nine current `endGameComparisons` entries in `Assets/Configs/game_settings.json` (deserialized by `src/Game.Configs/GameSettings.cs`'s `List<EndGameComparisonEntry> EndGameComparisons`, class at line 118), then round each to the nearest integer (`Math.Round(..., AwayFromZero)`, matching the existing policy). Current shipped values ascend from `14349` (`CommitteeOf300`) to `344365` (`BohemianGrove`); decreased values ascend from `11479` to `275492`.
  - `src/Game.Tests/EndGameThresholdFormulaTests.cs`'s `shipped_end_game_comparisons_match_formula_against_calibration_maximum` test hard-asserts each shipped score equals `Round(factor(i) × ShippedCalibrationMaximum, AwayFromZero)` with `ShippedCalibrationMaximum = 286971.0094511145` (line 15). Multiply `ShippedCalibrationMaximum` by the same `0.8` (→ `229576.8075608916`) and update the test constant to match; do not touch `factor(i)` or the rounding policy itself.
  - `EndGameComparisonProjector.Build` (`src/Game.Main/EndGameComparisonProjector.cs`) and `Assets/Scripts/Unity/UI/EndGameWindowView.cs.RefreshComparison` consume `EndGameComparisons` as-is and need no code change — only the config values (and the calibration constant/test above) change.
- Deriving the score-count goal value:
  - New goal = `Max(EndGameComparisons[i].Score) + 100` = `275492 + 100` = `275592`, computed once from the already-decreased entries and shipped as a static constant (see below) — not recomputed live at runtime.
  - `GameLogic.cs:95` already holds a loaded `GameSettings settings` instance at the point it calls `CompletionConditionFactory.Create(settings.CompletionCondition, MaxControlPool)`; the static goal constant is baked into the `CompletionConditionConfig`'s `Value` for the `score_goal` entry, consumed the same way `total_control`/`full_control_countries` consume their configured `Value`.
- New completion-condition type (`score_goal` / `ScoreGoal`):
  - Add `score_goal` to `src/Game.Configs/CompletionConditionType.cs`'s enum and `CompletionConditionTypeParser.TryParse`, alongside `total_control` / `full_control_countries`.
  - Add a `CompletionConditionConfig` entry (`src/Game.Configs/CompletionConditionConfig.cs`) under `GameSettings.CompletionCondition`'s existing `any` composite (`src/Game.Configs/GameSettings.cs` lines 36-42) as a third OR branch alongside `total_control >= 0.8` and `full_control_countries >= 15`, with `Value = 275592`.
  - Add a new `ScoreGoalCondition : ICompletionCondition` (pattern per `src/Game.Systems/TotalControlCondition.cs`) implementing `IsMet(CompletionConditionContext)` (`src/Game.Systems/ICompletionCondition.cs`). Read the organization's score via `GS.Game.Systems.ResourceQuery.GetValue(context.World, context.OrganizationId, GS.Game.Configs.ResourceDefinitions.OrgScore)` — `org_score` is a collector-driven `Resource` (not a standalone `Score` component; see `.claude/rules/unity/ecs_patterns.md`'s `[Savable]`/composition notes) — and compare (`>=`) against the goal threshold baked into the instance at construction time (mirroring how `TotalControlCondition`/`FullControlCondition` bake in their configured `Value`).
  - Wire the new case into `src/Game.Systems/CompletionConditionFactory.cs`'s `Create` switch (add a `CompletionConditionType.ScoreGoal` branch alongside `TotalControl`/`FullControlCountries`).
  - `src/Game.Systems/GameCompletionSystem.cs.Update` needs no change — it already evaluates whatever `ICompletionCondition` it is given per participant and assigns winner/losers generically, including the existing deterministic tie-break order for simultaneous threshold-meets.
- Goal-hint UI on the organization-selection screen:
  - Add a new `WinConditionHintKind` case (`src/Game.Main/VisualState.cs` line 880) for the score goal, and a matching branch in `WinConditionHintProjector.Flatten` (`src/Game.Main/WinConditionHintProjector.cs`, alongside the existing `TotalControl`/`FullControlCountries` cases at lines 30-35) that emits a row carrying the numeric goal value (`275592`).
  - Add a localized row format (new `select_org.win_conditions.*` key) to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, following the `localization` skill for the real Russian translation, per the pattern the existing `Control 80% of the World` / `Control completely at least 15/20 countries` rows use (see `Docs/Specs/26_07_22_16_end-game-window-goal-hint/spec.md`'s Tech Notes for the panel wiring, `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`/`.uss`, `SelectOrgDocument`).
- End-game presentation:
  - No new win/lose copy, `WinnerOrganizationId`/`OrganizationGameResult` handling, or `EndGameWindowView` change is needed — `src/Game.Components/GameCompletion.cs`, `OrganizationGameOutcome.cs`, and the existing end-game window bind to the generic completion result regardless of which `ICompletionCondition` triggered it.
- Verification:
  - Extend `EndGameThresholdFormulaTests.cs` (or add a sibling test file) covering: the decreased `endGameComparisons` values match the new calibration-maximum constant, the derived score-goal constant equals `max(decreasedScores) + 100`, `ScoreGoalCondition.IsMet` threshold/equality/below-threshold behavior, and factory wiring for the `score_goal` type — mirroring the coverage style of `Docs/Specs/26_07_22_11_win-lose-logic/spec.md`'s "Verification coverage" tech note. Unit-level coverage only — no calibration/regression pass analogous to a playthrough-reachability check is required.

## Out of Scope

- Changing the threshold formula (`factor(i) = 0.05 + i × (1.20 - 0.05) / 8`), the number of comparison entries, their identities/research/localization text, or the calibration skill's scenario/command mechanics themselves.
- Changing `total_control` or `full_control_countries` condition semantics, thresholds, or evaluation order.
- New end-game presentation, win/lose copy, animation, sound, or a "how you won" explanation distinguishing a score-count win from a control-based win.
- Any change to how `org_score` itself is computed, collected, or displayed on the leaderboard.
- A player-facing live progress indicator (e.g. "X / goal" score meter) beyond the static goal value shown once on the organization-selection win-conditions panel.

## Resolved Decisions

Owner answers from the issue #112 comment thread (2026-08-02):

- The 20% decrease is a flat `× 0.8` multiplier applied directly to the nine currently shipped `endGameComparisons` values, with `ShippedCalibrationMaximum` scaled the same way to keep `EndGameThresholdFormulaTests` passing.
- The decreased comparison scores are rounded to the nearest integer (matching the existing `Math.Round(..., AwayFromZero)` policy the original nine values were generated with).
- The new `score_goal` condition joins the existing default `any` group, OR-combined with `total_control >= 0.8` and `full_control_countries >= 15` — any one of the three wins the game.
- The score-goal threshold is a static shipped constant, computed once when the comparison scores are decreased and stored as a literal `Value` in `CompletionConditionConfig`, matching how `total_control`/`full_control_countries` already store literal configured values.
- Reaching the score goal gets its own `WinConditionHintKind`/localized row text on the pre-game win-conditions panel (all visual pieces required); no distinct end-game win/lose copy.
- Unit-level coverage of the threshold arithmetic and `ScoreGoalCondition` is sufficient — no calibration/regression pass analogous to `EndGameThresholdFormulaTests`' playthrough-reachability style is required for this condition type.
