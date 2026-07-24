# Calibration results

Recorded by the `calibration-run` task in `.ralph/prd.md` against the committed
`Assets/Configs/*.json`, using the fixed seed/org from `SKILL.md`.

## Inputs

| Field | Value |
|---|---|
| Config dir | `Assets/Configs` |
| Scored org (`--org`) | `Illuminati` |
| Seed (`--seed`) | `12345` |

Commands:

```
dotnet run --project src/Game.ConsoleRunner -c Release -- calibrate-end-game --config Assets/Configs --scenario win --org Illuminati --seed 12345 --output .claude/skills/end-game-score-calibration/references/win_result.json
dotnet run --project src/Game.ConsoleRunner -c Release -- calibrate-end-game --config Assets/Configs --scenario lose --org Illuminati --seed 12345 --output .claude/skills/end-game-score-calibration/references/lose_result.json
```

## Outputs

| Scenario | Winner org | Completed | Ticks | Final date | Score |
|---|---|---|---|---|---|
| win | Illuminati | true | 1 | 1880-01-02 | 286971.0094511145 |
| lose | Masons | true | 1 | 1880-01-02 | 3609.0095386261564 |

Both runs reached `GameLogic.IsCompleted` within the timeout ceiling. Re-running the `win`
scenario with identical inputs reproduced the exact same score bit-for-bit
(`286971.0094511145`), confirming determinism from the fixed seed.

### Harness fix applied during this run

The first pass of both scenarios returned an identical score (`3609.0095386261564`) for
`win` and `lose`, which is wrong — a total-control `win` run should score far higher than a
`lose` run. Root cause: `GameLogic.Update` runs `ResourceSystem.Update` (which recomputes
`org_score` from `ControlEffect` state) *before* applying that tick's `ChangeControlCommand`s,
and `GameCompletionSystem` (which detects the win and sets `IsCompleted`) runs *after* — so on
the single tick where control is granted and the game completes, `org_score` is read one tick
stale, and `GameLogic.Update` never runs again once `IsCompleted` is true. Fixed in
`src/Game.ConsoleRunner/CalibrationRunner.cs` by settling the score directly via
`OrgScoreCollector.Compute(orgId, currentValue, world)` after the run loop, instead of trusting
`ResourceQuery.GetValue` to already reflect the post-completion control state. This only affects
the calibration harness — no gameplay system was touched.

## Calibration maximum

The higher of the two scenarios' scores: **286971.0094511145** (the `win` scenario).

## Threshold formula

```
factor(i) = 0.05 + i * (1.20 - 0.05) / 8   for i = 0..8
threshold(i) = Math.Round(factor(i) * 286971.0094511145, MidpointRounding.AwayFromZero)
```

| i | factor(i) | threshold(i) |
|---|---|---|
| 0 | 0.05000 | 14349 |
| 1 | 0.19375 | 55601 |
| 2 | 0.33750 | 96853 |
| 3 | 0.48125 | 138105 |
| 4 | 0.62500 | 179357 |
| 5 | 0.76875 | 220609 |
| 6 | 0.91250 | 261861 |
| 7 | 1.05625 | 303113 |
| 8 | 1.20000 | 344365 |

These nine thresholds are not yet written into `Assets/Configs/game_settings.json`'s
`endGameComparisons` — populating the nine entries also requires the
`comparisonElementId` identities from the Google Trends research this headless run cannot
perform (see `threshold-formula-test` and the plan's Automation Notes).
