---
name: end-game-score-calibration
description: Run the calibrate-end-game console-runner CLI verb to derive the end-game comparison score thresholds for Assets/Configs/game_settings.json's endGameComparisons, and to re-derive them when config or score rules change.
---

# End-game score calibration

`calibrate-end-game` is a `Game.ConsoleRunner` CLI verb (`src/Game.ConsoleRunner/CalibrationRunner.cs`,
`src/Game.ConsoleRunner/CalibrationOptions.cs`) that drives a full headless game to completion and
records the scored organization's final `org_score` resource value. It exists to produce a reproducible
"calibration maximum" score, which the nine `endGameComparisons` entries in `game_settings.json` are
scaled against via the threshold formula below.

## Command

```
dotnet run --project src/Game.ConsoleRunner -- calibrate-end-game --config <dir> --scenario win|lose --org <id> --seed <n> --output <path>
```

- `--config`: config directory, e.g. `Assets/Configs` (the same directory `HeadlessRunner`/`Program.BuildContext` reads).
- `--scenario`: `win` drives `--org` itself to a total-control victory; `lose` drives a *different*
  organization to victory while `--org` stays a losing participant for the whole run — the score is
  always read from `--org`, so `lose` measures how high a losing org's score can climb.
- `--org`: the organization id whose `org_score` is recorded. Fixed calibration org: `Illuminati`
  (see `Assets/Configs/organizations.json`; the only other organization is `Masons`, which is used
  automatically as the winner for the `lose` scenario since it's the only other participant).
- `--seed`: fixed calibration seed: `12345`. Same config + same seed reproduces the same score bit-for-bit
  (`GameLogicContext.rngSeed` seeds a single `System.Random` used for all non-deterministic systems).
- `--output`: JSON result path, conventionally under `.claude/skills/end-game-score-calibration/references/`.

Optional flags (defaults match `HeadlessOptions`'s pattern): `--max-ticks` (default `20000`),
`--timeout-seconds` (default `300`), `--hours-per-tick` (default `24`).

## What it does

1. Loads `organizations.json`, resolves the winner org (`--org` for `win`, the other participating org
   for `lose`), and builds a `GameLogicContext` via `Program.BuildContext` with all organizations
   participating, `initialOrganizationId = --org`.
2. Runs one `GameLogic.Update(0f)` to initialize, pushes `DebugDiscoverAllCountriesCommand`, then pushes
   a `ChangeControlCommand` giving the winner org `MaxControlPool` control of every country in
   `country_config.json` (the same pattern `GameCompletionLogicTests.GiveTotalControl` uses in
   `src/Game.Tests`).
3. Calls `GameLogic.Update(deltaTime)` once per tick until `GameLogic.IsCompleted`, or until it hits
   `--max-ticks` or `--timeout-seconds` (checked every 256 ticks, same cadence as `HeadlessRunner`).
4. Reads the final score, settling it first rather than trusting a plain `ResourceQuery.GetValue`
   read: `GameCompletionSystem` can complete the game the same tick `ChangeControlCommand` is applied,
   one step after `ResourceSystem.Update` already ran with the pre-command control state, which would
   otherwise leave `org_score` one tick stale forever (`Update` short-circuits once `IsCompleted`). It
   reads the stale value via `ResourceQuery.GetValue(logic.World, --org, ResourceDefinitions.OrgScore)`
   and settles it via `new OrgScoreCollector().Compute(--org, staleValue, logic.World)` — the same
   collector formula `ResourceSystem` would have applied on the next, never-run tick — then writes a
   `CalibrationResult` JSON (`scenario`, `orgId`, `winnerOrgId`, `seed`, `completed`, `tickCount`,
   `finalDate`, `score`) to `--output`.

## Calibration maximum and threshold formula

Run both scenarios with the fixed seed/org above. The **calibration maximum** is the higher of the two
scenarios' recorded `score` values. The nine `endGameComparisons` entries are scaled off that maximum
using:

```
factor(i) = 0.05 + i * (1.20 - 0.05) / 8   for i = 0..8
threshold(i) = Math.Round(factor(i) * calibrationMaximum, MidpointRounding.AwayFromZero)
```

This produces nine ascending thresholds spanning 5% to 120% of the calibration maximum.

## Rerun / update procedure

Re-run both scenarios (and update `references/calibration_results.md`) whenever `game_settings.json`'s
score-affecting fields (`CountryScoreCoefficient`, `MaxControlPool`, `CompletionCondition`, resource
collector coefficients) or `country_config.json`'s country list change — any of those can shift the
calibration maximum:

```
dotnet run --project src/Game.ConsoleRunner -- calibrate-end-game --config Assets/Configs --scenario win --org Illuminati --seed 12345 --output .claude/skills/end-game-score-calibration/references/win_result.json
dotnet run --project src/Game.ConsoleRunner -- calibrate-end-game --config Assets/Configs --scenario lose --org Illuminati --seed 12345 --output .claude/skills/end-game-score-calibration/references/lose_result.json
```

Confirm both runs report `"completed": true` within the timeout ceiling, confirm re-running each
scenario reproduces the same `score` bit-for-bit, then update the calibration maximum and the nine
computed thresholds recorded in `references/calibration_results.md`.
