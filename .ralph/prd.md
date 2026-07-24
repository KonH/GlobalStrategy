# Ralph PRD — End Game Window and Goal Hint (headless subset)

Add the pure-C# core of the end-game presentation layer and pre-game goal-hint projection —
config schema, `src/Game.Main` projectors, their tests, the calibration console-runner CLI verb
and its documented skill, an actual calibration run against the committed config, a deterministic
threshold-formula test, `SelectOrgLogic`'s projector wiring, and the non-identity-dependent
localization keys — everything from the approved plan that has a real headless gate
(`dotnet build`/`dotnet test` or a Python validation script) with no Unity Editor/MCP available.
Source: [approved spec and plan](../Docs/Specs/26_07_22_16_end-game-window-goal-hint/).

This is a **subset** of the full plan. Unity asset/scene/prefab work, any `Assets/Scripts/`
C# change with no `src/` counterpart, and the Google-Trends identity research are excluded —
see `## Automation Notes` in the plan file for the verbatim list of what a human finishes later.

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`,
  flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail —
  a shell command), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "config-schema",
		"description": "Add the end-game comparison entry config schema (empty placeholder array).",
		"steps": [
			"Add public class EndGameComparisonEntry { public string ComparisonElementId { get; set; } = \"\"; public double Score { get; set; } } to src/Game.Configs/GameSettings.cs.",
			"Add public List<EndGameComparisonEntry> EndGameComparisons { get; set; } = new List<EndGameComparisonEntry>(); to GameSettings.",
			"Add the matching empty endGameComparisons: [] array (camelCase) to Assets/Configs/game_settings.json."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "gamelogic-settings",
		"description": "Expose the loaded GameSettings instance from GameLogic for downstream src/ consumers.",
		"steps": [
			"In src/Game.Main/GameLogic.cs, add public GameSettings GameSettings { get; private set; } and assign it from the existing local `settings` variable in the constructor (same pattern already used for ResourceConfig/CharacterConfig/etc.).",
			"Do not touch Assets/Scripts/Unity/DI/GameLifetimeScope.cs in this task — that registration requires Unity and is out of scope for this headless run."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "comparison-projector",
		"description": "Add EndGameComparisonRowState and the pure EndGameComparisonProjector.",
		"steps": [
			"Add EndGameComparisonRowState (Place, ComparisonElementId, IsPlayer, DisplayName, Score) to src/Game.Main/VisualState.cs, mirroring LeaderboardEntryState's constructor shape.",
			"Add src/Game.Main/EndGameComparisonProjector.cs with static List<EndGameComparisonRowState> Build(IReadOnlyList<EndGameComparisonEntry> configuredEntries, string playerOrgId, string playerDisplayName, double playerScore).",
			"Behavior: one row per configured entry (IsPlayer=false) plus one player row (IsPlayer=true); sort descending by Score; tie-break by ComparisonElementId ordinal with IsPlayer as a documented deterministic secondary key; assign 1-based sequential Place; null/empty configuredEntries yields a single player-only row."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "goal-hint-projector",
		"description": "Add WinConditionHintState/row model and the pure WinConditionHintProjector.",
		"steps": [
			"Add WinConditionHintKind (TotalControl, FullControlCountries), WinConditionHintRowState (Kind, Value, AvailableCountryCount), and WinConditionHintState (IsAvailable, IsAlternativeGroup, Rows, Set(...)) to src/Game.Main/VisualState.cs; add public WinConditionHintState WinConditionHint { get; } = new WinConditionHintState(); to VisualState.",
			"Add src/Game.Main/WinConditionHintProjector.cs with static (bool isAvailable, bool isAlternativeGroup, List<WinConditionHintRowState> rows) Build(CompletionConditionConfig? condition, int availableCountryCount).",
			"Behavior: recursively flatten condition.Type == \"any\" preserving configuration order depth-first; map total_control and full_control_countries leaves to typed rows; silently skip unsupported leaf types; null/empty/all-unsupported condition yields (false, false, []); isAlternativeGroup is true only when 2+ rows result."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "projector-tests",
		"description": "Add core tests for both new pure projectors with synthetic fixtures.",
		"steps": [
			"Add src/Game.Tests/EndGameComparisonProjectorTests.cs: player inserted among N entries and sorted descending; deterministic reproducible tie-break; null/empty entries yield a single player-only row; scores/ids pass through unchanged; places are 1-based and consecutive.",
			"Add src/Game.Tests/WinConditionHintProjectorTests.cs: single total_control leaf; single full_control_countries leaf with a supplied available-country count; nested any groups flatten to correct order regardless of depth; unsupported leaf types are skipped without failing the projection; null condition, empty any, and any-with-only-unsupported-leaves all yield isAvailable==false with zero rows; isAlternativeGroup true only for 2+ rows.",
			"Confirm existing LeaderboardEntryState/SortAndAssignPlaces, GameCompletion/GameCompletionSystem, and SelectOrgLogicTests suites still pass unmodified."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "calibration-runner",
		"description": "Add the calibration console-runner CLI verb and document it in a skill.",
		"steps": [
			"Add src/Game.ConsoleRunner/CalibrationRunner.cs with a method taking a config dir, scenario (win/lose), and target org id, reusing Program.BuildContext exactly like HeadlessRunner does.",
			"Add a new Program.cs CLI verb calibrate-end-game --config <dir> --scenario win|lose --org <id> --seed <n> --output <path> that drives the scenario via DebugDiscoverAllCountriesCommand + ChangeControlCommand deltas + GameLogic.Update per tick until GameLogic.IsCompleted, with a hard tick-count/timeout ceiling matching HeadlessOptions.TimeoutSeconds's existing pattern, reading the score via ResourceQuery.GetValue(logic.World, orgId, ResourceDefinitions.OrgScore), and writing a JSON result (final score, tick count, terminal date) to --output.",
			"Add .claude/skills/end-game-score-calibration/SKILL.md (YAML frontmatter name/description + numbered instructions, matching .claude/skills/dotnet-test/SKILL.md's convention) documenting: the exact build/run command; fixed seed/org/HQ config taken from the committed Assets/Configs/*.json; the debug-command sequence and terminal assertion; win vs. lose scenario framing (lose scenario drives a different org to the winning threshold while the scored org stays a losing participant); the score read-out call; that the higher of the two scenarios' recorded score is the calibration maximum; the threshold formula factor(i) = 0.05 + i * (1.20 - 0.05) / 8 for i = 0..8 with MidpointRounding.AwayFromZero; output paths under .claude/skills/end-game-score-calibration/references/; and the rerun/update procedure for when config or score rules change."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "calibration-run",
		"description": "Run the calibrate-end-game CLI for both scenarios against the real committed config and commit reproducible evidence.",
		"steps": [
			"Run `dotnet run --project src/Game.ConsoleRunner -- calibrate-end-game --config Assets/Configs --scenario win --org <id> --seed <n> --output <path>` and the equivalent --scenario lose run, using the fixed seed/org documented in the SKILL.md from the previous task.",
			"Confirm both runs reach GameLogic.IsCompleted within the timeout ceiling and re-running each scenario reproduces the same final score bit-for-bit (deterministic seed).",
			"Record the calibration maximum (the higher of the two scenarios' scores) and both scenarios' full inputs/outputs (command lines, seeds, final scores, tick counts) under .claude/skills/end-game-score-calibration/references/calibration_results.md (or .json + short .md summary)."
		],
		"gate": ".venv\\Scripts\\python.exe .tmp/verify_calibration.py",
		"passes": true
	},
	{
		"category": "threshold-formula-test",
		"description": "Add a deterministic test for the endGameComparisons threshold formula and rounding policy using a synthetic calibration maximum.",
		"steps": [
			"Add a test (in src/Game.Tests, alongside EndGameComparisonProjectorTests or in a new small fixture) that computes factor(i) = 0.05 + i * (1.20 - 0.05) / 8 for i = 0..8 against a fixed synthetic calibration maximum, applies MidpointRounding.AwayFromZero, and asserts the nine results are strictly ascending and match hand-computed expected values.",
			"This intentionally does not touch Assets/Configs/game_settings.json's real endGameComparisons values or comparisonElementId identities — populating those requires the Google Trends research this headless run cannot perform; leave the placeholder empty array from the first task as-is."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "select-org-logic",
		"description": "Wire WinConditionHintProjector into SelectOrgLogic's one-time construction.",
		"steps": [
			"Extend src/Game.Main/SelectOrgLogic.cs's constructor to accept IConfigSource<GameSettings> gameSettingsConfig, load it once, compute availableCountryCount from the already-loaded country config entries' IsAvailable, call WinConditionHintProjector.Build(settings.CompletionCondition, availableCountryCount), and set VisualState.WinConditionHint.Set(...) once in the constructor — matching the existing one-time-build pattern already used for HqCountryIds in this class.",
			"Do not touch Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs in this task — wiring the new TextAsset field and updated constructor call requires Unity and is out of scope for this headless run.",
			"Update or add to src/Game.Tests SelectOrgLogicTests as needed so the extended constructor signature is covered without breaking existing passing tests."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "localization-keys",
		"description": "Add the non-identity-dependent end_game.* and select_org.win_conditions.* localization keys to en.asset and ru.asset.",
		"steps": [
			"Add end_game.result.win, end_game.result.lose (format templates taking the org display name as {0}), end_game.exit, and end_game.leaderboard.empty to both Assets/Localization/en.asset and Assets/Localization/ru.asset.",
			"Add select_org.win_conditions.header, select_org.win_conditions.alternative_cue, select_org.win_conditions.empty, select_org.win_conditions.total_control, and select_org.win_conditions.full_control_countries to both assets.",
			"Do not add end_game.comparison.<comparisonElementId> keys in this task — the nine identities depend on the Google Trends research this headless run cannot perform.",
			"Write and run a Python script (.venv\\Scripts\\python.exe .tmp/verify_localization_keys.py) that parses both .asset files and confirms every new key above exists in both files with a non-empty value, then delete the script per .claude/rules/temp_scripts.md."
		],
		"gate": ".venv\\Scripts\\python.exe .tmp/verify_localization_keys.py",
		"passes": false
	},
	{
		"category": "final-verification",
		"description": "Run the full core test suite and Release build as the final headless verification pass.",
		"steps": [
			"Run dotnet test src/GlobalStrategy.Core.sln and confirm all tests pass, including the new projector, threshold-formula, and SelectOrgLogic tests.",
			"Run dotnet build src/GlobalStrategy.Core.sln -c Release and confirm a clean build.",
			"Unity-side refresh/console verification (Assets/Scripts/Unity, EndGameWindow UI, SelectCountry goal-hint markup, scene wiring) is out of scope for this headless run — left for a human pass, see plan.md's Automation Notes."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	}
]
```
