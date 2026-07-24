# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-23 — config-schema

Task: Add the end-game comparison entry config schema (empty placeholder array).

Changes:
- `src/Game.Configs/GameSettings.cs`: added `EndGameComparisonEntry` class
  (`ComparisonElementId`, `Score`) and `List<EndGameComparisonEntry> EndGameComparisons`
  property on `GameSettings`.
- `Assets/Configs/game_settings.json`: added `"endGameComparisons": []`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `gamelogic-settings` task — expose `GameSettings` from
`GameLogic` in `src/Game.Main/GameLogic.cs` (do not touch
`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, out of scope for headless run).

---

## 2026-07-23 — gamelogic-settings

Task: Expose the loaded GameSettings instance from GameLogic for downstream src/ consumers.

Changes:
- `src/Game.Main/GameLogic.cs`: added `public GameSettings GameSettings { get; private set; }`
  property, assigned from the existing local `settings` variable right after
  `context.GameSettings.Load()` in the constructor (same pattern as ResourceConfig/CharacterConfig/etc.).
  Did not touch `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` (Unity, out of scope).

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `comparison-projector` task — add `EndGameComparisonRowState` to
`src/Game.Main/VisualState.cs` and `src/Game.Main/EndGameComparisonProjector.cs`.

---

## 2026-07-23 — comparison-projector

Task: Add EndGameComparisonRowState and the pure EndGameComparisonProjector.

Changes:
- `src/Game.Main/VisualState.cs`: added `EndGameComparisonRowState` (Place,
  ComparisonElementId, IsPlayer, DisplayName, Score), mirroring `LeaderboardEntryState`'s
  constructor shape. Not registered on `VisualState` — the task only specifies the row
  model + projector, not a wrapping `INotifyPropertyChanged` state (unlike the upcoming
  goal-hint task, which explicitly adds `WinConditionHintState` to `VisualState`).
- `src/Game.Main/EndGameComparisonProjector.cs`: new static `Build(configuredEntries,
  playerOrgId, playerDisplayName, playerScore)` returning
  `List<EndGameComparisonRowState>`. One row per configured entry (IsPlayer=false) plus
  one player row (IsPlayer=true); sorted descending by Score, tie-break by
  ComparisonElementId ordinal, then IsPlayer (false before true) as the deterministic
  secondary/tertiary key; 1-based sequential Place assigned after sort. Null
  `configuredEntries` yields a single player-only row (no null-check needed for empty
  lists since the loop is a no-op).

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `goal-hint-projector` task — add `WinConditionHintKind`,
`WinConditionHintRowState`, `WinConditionHintState` (registered on `VisualState`) to
`src/Game.Main/VisualState.cs` and `src/Game.Main/WinConditionHintProjector.cs`.

---

## 2026-07-23 — goal-hint-projector

Task: Add WinConditionHintState/row model and the pure WinConditionHintProjector.

Changes:
- `src/Game.Main/VisualState.cs`: added `WinConditionHintKind` enum (TotalControl,
  FullControlCountries), `WinConditionHintRowState` (Kind, Value, AvailableCountryCount),
  and `WinConditionHintState : INotifyPropertyChanged` (IsAvailable, IsAlternativeGroup,
  Rows, `Set(...)`), following the same private-setter + `PropertyChanged` pattern as
  `GameCompletionState`/`LeaderboardState`. Registered `WinConditionHintState
  WinConditionHint` on `VisualState`.
- `src/Game.Main/WinConditionHintProjector.cs`: new static `Build(CompletionConditionConfig?
  condition, int availableCountryCount)` returning `(bool isAvailable, bool
  isAlternativeGroup, List<WinConditionHintRowState> rows)`. Recursively flattens
  `condition.Type == "any"` depth-first in `Members` order; maps `total_control` and
  `full_control_countries` leaves to typed rows; unknown leaf types are silently skipped
  (no row added); null condition or an all-unsupported tree yields `(false, false, [])`;
  `isAlternativeGroup` is true only when 2+ rows result.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `projector-tests` task — add
`src/Game.Tests/EndGameComparisonProjectorTests.cs` and
`src/Game.Tests/WinConditionHintProjectorTests.cs`, gate `dotnet test
src/GlobalStrategy.Core.sln`.

---

## 2026-07-23 — projector-tests

Task: Add core tests for both new pure projectors with synthetic fixtures.

Changes:
- `src/Game.Tests/EndGameComparisonProjectorTests.cs`: player inserted among N
  configured entries and sorted descending by score; deterministic reproducible
  tie-break by ComparisonElementId ordinal (re-running `Build` with the same inputs
  yields identical ordering); null and empty `configuredEntries` both yield a
  single player-only row; scores/ids/display names pass through unchanged;
  `Place` is 1-based and consecutive. Needed `Assert.NotNull` + null-forgiving `!`
  on `List.Find` results to satisfy nullable-reference-type analysis (CS8602)
  since `EndGameComparisonRowState` is a class and `Find` can return null.
- `src/Game.Tests/WinConditionHintProjectorTests.cs`: single `total_control` leaf;
  single `full_control_countries` leaf carrying the supplied available-country
  count; nested `any` groups flatten depth-first in configuration order regardless
  of nesting depth; unsupported leaf types are silently skipped without failing
  the projection; null condition, empty `any`, and an `any` with only unsupported
  leaves all yield `isAvailable == false` with zero rows; `isAlternativeGroup` is
  true only for 2+ resulting rows.
- Did not modify any existing test file — `LeaderboardEntryState`/
  `GameCompletion*`/`SelectOrgLogicTests` suites were confirmed still passing via
  the full-solution test run (no changes needed to keep them green).

Gate: `dotnet test src/GlobalStrategy.Core.sln` — `Passed! - Failed: 0, Passed:
371, Skipped: 0, Total: 371` (Game.Tests.dll), plus ECS.Tests (34/34) and
ECS.Viewer.Tests (16/16) all green.

Next iteration: pick up `calibration-runner` task — add
`src/Game.ConsoleRunner/CalibrationRunner.cs`, a new `calibrate-end-game` CLI verb
in `Program.cs`, and `.claude/skills/end-game-score-calibration/SKILL.md`.

---

## 2026-07-23 — calibration-runner

Task: Add the calibration console-runner CLI verb and document it in a skill.

Changes:
- `src/Game.ConsoleRunner/CalibrationOptions.cs`: new options parser for the
  `calibrate-end-game` verb (`--config`, `--scenario win|lose`, `--org`, `--seed`,
  `--output`, plus `--max-ticks`/`--timeout-seconds`/`--hours-per-tick` overrides),
  following `HeadlessOptions`'s `NextArg`/`ParseIntArg` pattern.
- `src/Game.ConsoleRunner/CalibrationResult.cs`: new JSON result DTO (scenario, orgId,
  winnerOrgId, seed, completed, tickCount, finalDate, score).
- `src/Game.ConsoleRunner/CalibrationRunner.cs`: new `Run(CalibrationOptions)`. Resolves
  the winner org (`--org` itself for `win`; the other participating org for `lose`, so the
  scored `--org` stays a losing participant), builds a `GameLogicContext` via
  `Program.BuildContext` with all organizations participating, runs one `Update(0f)` to
  init, pushes `DebugDiscoverAllCountriesCommand` plus a `ChangeControlCommand` per
  `country_config.json` entry giving the winner org `MaxControlPool` control (mirroring
  `GameCompletionLogicTests.GiveTotalControl`), then loops `GameLogic.Update(deltaTime)`
  per tick until `IsCompleted`, capped by `--max-ticks` (default 20000) and
  `--timeout-seconds` (default 300, checked every 256 ticks — same cadence as
  `HeadlessRunner`). Reads the final score via
  `ResourceQuery.GetValue(logic.World, options.OrgId, ResourceDefinitions.OrgScore)` and
  writes the JSON result to `--output`.
- `src/Game.ConsoleRunner/Program.cs`: `Main` now checks `args[0] ==
  "calibrate-end-game"` before the existing `HeadlessOptions.Parse` path and dispatches
  to `CalibrationRunner.Run`.
- `.claude/skills/end-game-score-calibration/SKILL.md`: new skill documenting the exact
  command, the fixed calibration seed (`12345`) and org (`Illuminati`, the only other
  organization `Masons` being the automatic `lose`-scenario winner per
  `Assets/Configs/organizations.json`), the debug-command/control-command sequence, the
  win/lose scenario framing, the score read-out call, the calibration-maximum definition
  (higher of the two scenarios' scores), the threshold formula `factor(i) = 0.05 + i *
  (1.20 - 0.05) / 8` for `i = 0..8` with `MidpointRounding.AwayFromZero`, the
  `references/` output convention, and the rerun/update procedure.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0
errors (log deleted per `.claude/rules/temp_scripts.md`).

Next iteration: pick up `calibration-run` task — actually run
`calibrate-end-game` for both `win` and `lose` scenarios against
`Assets/Configs` with seed `12345`/org `Illuminati` (per the new SKILL.md), verify
`GameLogic.IsCompleted` is reached and re-runs reproduce the same score, and record
both scenarios' inputs/outputs plus the calibration maximum under
`.claude/skills/end-game-score-calibration/references/calibration_results.md`. That
task's gate is a not-yet-written `.tmp/verify_calibration.py` — write it fresh each
run per `.claude/rules/temp_scripts.md` (it isn't checked in, `.tmp/` is gitignored).

## 2026-07-23 - Ralph loop error (phase: loop, iteration: 6)

claude exited with code 1. See `.ralph/logs/loop_6_20260723_205853.log` for full stdout/stderr.

Summary: {"type":"result","subtype":"success","is_error":true,"api_error_status":429,"duration_ms":260984,"duration_api_ms":236207,"num_turns":47,"result":"You've hit your session limit · resets 9:20pm (UTC)","stop_reason":"stop_sequence","session_id":"f2f5e64a-c747-4f20-8d06-dfc6448b95a9","total_cost_usd":1.5481580000000004,"usage":{"input_tokens":66,"cache_creation_input_tokens":84215,"cache_read_input_tokens":2495110,"output_tokens":19505,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},"service_tier":"standard","cache_creation":{"ephemeral_1h_input_tokens":84215,"ephemeral_5m_input_tokens":0},"inference_geo":"not_available","iterations":[{"input_tokens":2,"output_tokens":174,"cache_read_input_tokens":97828,"cache_creation_input_tokens":890,"cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":890},"type":"message"}],"speed":"standard"},"modelUsage":{"claude-haiku-4-5-20251001":{"inputTokens":1487,"outputTokens":15,"cacheReadInputTokens":0,"cacheCreationInputTokens":0,"webSearchRequests":0,"costUSD":0.001562,"contextWindow":200000,"maxOutputTokens":32000},"claude-sonnet-5":{"inputTokens":66,"outputTokens":19505,"cacheReadInputTokens":2495110,"cacheCreationInputTokens":84215,"webSearchRequests":0,"costUSD":1.546596,"contextWindow":1000000,"maxOutputTokens":64000}},"permission_denials":[],"terminal_reason":"api_error","fast_mode_state":"off","uuid":"e9990020-eb00-44ed-b5b8-7cde8a711f1d"}

---

## 2026-07-24 — calibration-run

Task: Run the calibrate-end-game CLI for both scenarios against the real committed config
and commit reproducible evidence.

Changes:
- Ran `dotnet run --project src/Game.ConsoleRunner -c Release -- calibrate-end-game
  --config Assets/Configs --scenario win --org Illuminati --seed 12345 --output
  .claude/skills/end-game-score-calibration/references/win_result.json` and the equivalent
  `--scenario lose` run. Both initially returned the *same* score
  (`3609.0095386261564`) for `win` and `lose`, which is wrong for a total-control win vs. a
  losing participant.
- Root-caused and fixed a bug in `src/Game.ConsoleRunner/CalibrationRunner.cs` (added in the
  prior `calibration-runner` task): `GameLogic.Update` (`src/Game.Main/GameLogic.cs:111-116`)
  runs `ResourceSystem.Update` (which recomputes `org_score` from `ControlEffect` state)
  *before* applying that tick's `ChangeControlCommand`s, while `GameCompletionSystem`
  (`GameLogic.cs:199`, which detects the win and flips `IsCompleted`) runs *after* — so on the
  single tick where total control is granted and the game completes, `ResourceQuery.GetValue`
  reads a one-tick-stale `org_score`, and `Update` never runs again once `IsCompleted` is true
  (short-circuits at `GameLogic.cs:92-96`). Fixed by settling the score directly via
  `OrgScoreCollector.Compute(orgId, currentValue, world)` after the run loop instead of
  trusting the stored resource value — this is exactly the delta `ResourceSystem` would have
  applied on the tick that never runs. Harness-only fix; no gameplay system touched.
- Re-ran both scenarios after the fix: `win` now scores `286971.0094511145` (Illuminati wins
  total control), `lose` still scores `3609.0095386261564` (Illuminati holds no countries
  either way, so the stale-vs-fresh read makes no difference there — confirms the fix targets
  the right bug). Re-ran `win` a third time with identical inputs: bit-for-bit identical score,
  confirming determinism from the fixed seed.
- Wrote `.claude/skills/end-game-score-calibration/references/calibration_results.md`
  recording both scenarios' full inputs/outputs, the harness-fix explanation, the calibration
  maximum (`286971.0094511145`, the `win` scenario), and the nine computed thresholds
  (`factor(i) = 0.05 + i * (1.20-0.05)/8`, `MidpointRounding.AwayFromZero`): 14349, 55601,
  96853, 138105, 179357, 220609, 261861, 303113, 344365.
- Wrote `.tmp/verify_calibration.py` (gate script — not checked in, `.tmp/` is gitignored):
  parses `win_result.json`/`lose_result.json`, checks `completed`/`seed`/`orgId`/`winnerOrgId`
  per scenario, asserts `win` score > `lose` score, and cross-checks
  `calibration_results.md` contains the calibration maximum and all nine threshold values.
  Deleted after running per `.claude/rules/temp_scripts.md`.
- Environment note: this container has no `.venv` (Windows-style `.venv\Scripts\python.exe`
  from the task's nominal gate doesn't apply here) — ran the verification script with the
  system `python3` instead, which is the direct Linux equivalent for a `full-env-headless` run.

Gate: `python3 .tmp/verify_calibration.py` — `OK: calibration_results.md and win/lose result
JSON are consistent. Calibration maximum: 286971.0094511145`. Also re-ran
`dotnet build src/GlobalStrategy.Core.sln -c Release` (0 warnings/errors) and
`dotnet test src/GlobalStrategy.Core.sln` (421/421 passing: Game.Tests 371, ECS.Tests 34,
ECS.Viewer.Tests 16) to confirm the `CalibrationRunner.cs` change didn't regress anything.

Next iteration: pick up `threshold-formula-test` task — add a deterministic test (in
`src/Game.Tests`) for `factor(i) = 0.05 + i * (1.20-0.05)/8` / `MidpointRounding.AwayFromZero`
against a fixed *synthetic* calibration maximum (not the real
`286971.0094511145` — that's just recorded evidence, the test fixture should use its own
simple round-number maximum for hand-computable expected values). Do not touch
`Assets/Configs/game_settings.json`'s `endGameComparisons` (still an empty placeholder —
populating it needs the Google Trends research this headless run can't do). Gate:
`dotnet test src/GlobalStrategy.Core.sln`.

---
