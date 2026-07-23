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
