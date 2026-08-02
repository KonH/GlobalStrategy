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
  - Apply a flat `× 0.8` multiplier to the nine current `endGameComparisons` entries in `Assets/Configs/game_settings.json` (deserialized by `src/Game.Configs/GameSettings.cs`'s `List<EndGameComparisonEntry> EndGameComparisons`, class at line 118). Current shipped values ascend from `14349` (`CommitteeOf300`) to `344365` (`BohemianGrove`); post-multiplier the max becomes `344365 × 0.8 = 275492` (exact rounding policy is an open question, see Ambiguities).
  - `src/Game.Tests/EndGameThresholdFormulaTests.cs`'s `shipped_end_game_comparisons_match_formula_against_calibration_maximum` test hard-asserts each shipped score equals `Round(factor(i) × ShippedCalibrationMaximum, AwayFromZero)` with `ShippedCalibrationMaximum = 286971.0094511145` (line 15). A flat `× 0.8` on the nine scores must be paired with multiplying `ShippedCalibrationMaximum` by the same `0.8` (→ `229576.80756089...`) and updating the test constant, or this test fails on drift; do not touch `factor(i)` or the rounding policy itself.
  - `EndGameComparisonProjector.Build` (`src/Game.Main/EndGameComparisonProjector.cs`) and `Assets/Scripts/Unity/UI/EndGameWindowView.cs.RefreshComparison` consume `EndGameComparisons` as-is and need no code change — only the config values (and the calibration constant/test above) change.
- Deriving the score-count goal value:
  - New goal = `Max(EndGameComparisons[i].Score) + 100`, computed from the already-decreased entries above (`≈ 275592` using the flat-multiplier example, exact figure depends on the rounding policy chosen).
  - Whether this is a static shipped constant recomputed alongside the comparison-score change, or computed live at runtime from `GameSettings.EndGameComparisons` each time the condition is constructed/evaluated, is an open question (see Ambiguities). `GameLogic.cs:95` already holds a loaded `GameSettings settings` instance at the point it calls `CompletionConditionFactory.Create(settings.CompletionCondition, MaxControlPool)`, so passing `settings.EndGameComparisons` (or a precomputed goal double) into the factory at the same call site is available either way without threading `GameSettings` further than it already reaches.
- New completion-condition type (`score_goal` / `ScoreGoal`):
  - Add `score_goal` to `src/Game.Configs/CompletionConditionType.cs`'s enum and `CompletionConditionTypeParser.TryParse`, alongside `total_control` / `full_control_countries`.
  - Add a `CompletionConditionConfig` entry (`src/Game.Configs/CompletionConditionConfig.cs`) under `GameSettings.CompletionCondition`'s existing `any` composite (`src/Game.Configs/GameSettings.cs` lines 36-42) — default config currently ORs `total_control >= 0.8` and `full_control_countries >= 15`; whether `score_goal` joins that OR group or replaces it is an open question (see Ambiguities).
  - Add a new `ScoreGoalCondition : ICompletionCondition` (pattern per `src/Game.Systems/TotalControlCondition.cs`) implementing `IsMet(CompletionConditionContext)` (`src/Game.Systems/ICompletionCondition.cs`). Read the organization's score via `GS.Game.Systems.ResourceQuery.GetValue(context.World, context.OrganizationId, GS.Game.Configs.ResourceDefinitions.OrgScore)` — `org_score` is a collector-driven `Resource` (not a standalone `Score` component; see `.claude/rules/unity/ecs_patterns.md`'s `[Savable]`/composition notes) — and compare against the goal threshold. `CompletionConditionContext` currently exposes `World`, `OrganizationId`, `AvailableCountryIds`, `MaxControlPool` only; it does not carry a score threshold or `GameSettings`, so either the threshold is baked into the `ScoreGoalCondition` instance at construction time (mirroring how `TotalControlCondition`/`FullControlCondition` bake in their configured `Value`) or the context needs a new field — construction-time baking matches the existing pattern most closely.
  - Wire the new case into `src/Game.Systems/CompletionConditionFactory.cs`'s `Create` switch (add a `CompletionConditionType.ScoreGoal` branch alongside `TotalControl`/`FullControlCountries`).
  - `src/Game.Systems/GameCompletionSystem.cs.Update` needs no change — it already evaluates whatever `ICompletionCondition` it is given per participant and assigns winner/losers generically.
- Goal-hint UI on the organization-selection screen:
  - Add a new `WinConditionHintKind` case (`src/Game.Main/VisualState.cs` line 880) for the score goal, and a matching branch in `WinConditionHintProjector.Flatten` (`src/Game.Main/WinConditionHintProjector.cs`, alongside the existing `TotalControl`/`FullControlCountries` cases at lines 30-35) that emits a row carrying the numeric goal value.
  - Add a localized row format (new `select_org.win_conditions.*` key) to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, following the `localization` skill for the real Russian translation, per the pattern the existing `Control 80% of the World` / `Control completely at least 15/20 countries` rows use (see `Docs/Specs/26_07_22_16_end-game-window-goal-hint/spec.md`'s Tech Notes for the panel wiring, `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`/`.uss`, `SelectOrgDocument`).
- End-game presentation:
  - No new win/lose copy, `WinnerOrganizationId`/`OrganizationGameResult` handling, or `EndGameWindowView` change is needed — `src/Game.Components/GameCompletion.cs`, `OrganizationGameOutcome.cs`, and the existing end-game window bind to the generic completion result regardless of which `ICompletionCondition` triggered it.
- Verification:
  - Extend `EndGameThresholdFormulaTests.cs` (or add a sibling test file) covering: the decreased `endGameComparisons` values match the new calibration-maximum constant, the derived score-goal constant equals `max(decreasedScores) + 100`, `ScoreGoalCondition.IsMet` threshold/equality/below-threshold behavior, and factory wiring for the `score_goal` type — mirroring the coverage style of `Docs/Specs/26_07_22_11_win-lose-logic/spec.md`'s "Verification coverage" tech note.
  - Whether this new condition type needs a `calibrate-end-game`-style calibration pass of its own (the existing skill only calibrates the nine comparison scores' scaling, not a win-condition threshold) is an open question (see Ambiguities).

## Out of Scope

- Changing the threshold formula (`factor(i) = 0.05 + i × (1.20 - 0.05) / 8`), the number of comparison entries, their identities/research/localization text, or the calibration skill's scenario/command mechanics themselves.
- Changing `total_control` or `full_control_countries` condition semantics, thresholds, or evaluation order.
- New end-game presentation, win/lose copy, animation, sound, or a "how you won" explanation distinguishing a score-count win from a control-based win.
- Any change to how `org_score` itself is computed, collected, or displayed on the leaderboard.
- A player-facing live progress indicator (e.g. "X / goal" score meter) beyond the static goal value shown once on the organization-selection win-conditions panel.

## Ambiguities

- [NEEDS CLARIFICATION: Is the 20% decrease a one-time flat `× 0.8` multiplier applied directly to the nine currently shipped `endGameComparisons` values (and `ShippedCalibrationMaximum` scaled the same way to keep `EndGameThresholdFormulaTests` passing), or should it be achieved by lowering `ShippedCalibrationMaximum` by 20% and re-running the `calibrate-end-game` formula/tooling — which could, in principle, produce different numbers than a flat multiplier if the underlying calibration run itself isn't purely linear in that constant? Assumed default: flat multiplier on the shipped numbers, calibration constant scaled to match.]
- [NEEDS CLARIFICATION: What rounding policy applies to the decreased comparison scores — keep full double precision (e.g. `11479.2`), or round to the nearest integer (matching the existing `Math.Round(..., AwayFromZero)` policy the original nine values were generated with)? The config currently stores whole numbers.]
- [NEEDS CLARIFICATION: Does the new `score_goal` condition join the existing default `any` group (OR-combined with `total_control >= 0.8` and `full_control_countries >= 15`, so any one of the three wins the game), or does it replace the existing two conditions as the sole win condition? Assumed default: OR-combined, since the issue describes it as an additional path to victory rather than a replacement.]
- [NEEDS CLARIFICATION: Should the score-goal threshold be a static shipped constant (computed once when the comparison scores are decreased, stored as a literal `Value` in `CompletionConditionConfig`), or computed live at runtime from `GameSettings.EndGameComparisons` every time the condition is constructed/evaluated so it can never silently drift out of sync with the comparison config? Assumed default: static shipped constant for consistency with how `total_control`/`full_control_countries` already store literal configured values, but this risks drift if `endGameComparisons` is edited later without updating it.]
- [NEEDS CLARIFICATION: Does reaching the score goal need its own `WinConditionHintKind`/localized row text and any end-game result copy distinguishing it from a control-based win, or is a generic win-conditions row plus the existing shared end-game win/lose presentation sufficient? Assumed default: new hint row for the pre-game panel (needed either way to disclose the numeric goal), but no distinct end-game win/lose copy.]
- [NEEDS CLARIFICATION: Does this new completion-condition type warrant calibration/regression coverage analogous to `EndGameThresholdFormulaTests` (e.g. asserting the goal is reachable within a bounded number of ticks in a normal playthrough), or is unit-level coverage of the threshold arithmetic and `ScoreGoalCondition` sufficient?]
