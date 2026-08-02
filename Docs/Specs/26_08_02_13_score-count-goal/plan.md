# Plan: Score Count Goal

## Spec

Source: `Docs/Specs/26_08_02_13_score-count-goal/spec.md`.

**Intent.** Add a new `score_goal` completion condition so a purely score-driven campaign has its
own path to victory, without requiring territorial control thresholds. The nine shipped end-game
comparison scores in `Assets/Configs/game_settings.json` are decreased by a flat `× 0.8` (rounded
to nearest integer, same policy as today), the goal value is derived once as
`max(decreasedScores) + 100 = 275592`, and a third OR branch (`score_goal`) joins the existing
`any` completion condition alongside `total_control >= 0.8` / `full_control_countries >= 15`. The
pre-game win-conditions hint panel gets a new localized row for the score goal.

**Acceptance criteria (summarized, see spec.md for the full legend-form list).**
- All nine shipped `endGameComparisons` scores are each exactly 20% lower (× 0.8, rounded
  away-from-zero), relative order unchanged; the end-game comparison block still ranks the player
  correctly among them.
- The score-count goal value equals the new highest comparison score + 100 (`275592`), computed
  once and shipped as a literal config value — not recomputed live.
- The organization-selection win-conditions panel shows a row describing the score-count goal
  before the game starts.
- With the `score_goal` condition included in the configured objective: no org meeting the goal
  keeps the game in progress; one org reaching or exceeding the goal makes it the sole winner
  (others become losers) the same tick, exactly like an existing control-based win; equality at
  the goal counts as met; simultaneous qualifiers resolve via the existing stable
  `ParticipationOrder` tie-break.
- A score-count win/lose reuses the existing end-game window presentation (leaderboard, comparison
  block, win/lose header) with no new "reason for victory" messaging.

## Goal

Wire `score_goal` through the same config → `ICompletionCondition` → factory → `any`-composite
pipeline that `total_control`/`full_control_countries` already use, decrease the shipped
comparison config by the agreed multiplier, and surface the new goal as a localized hint row —
all without touching `GameCompletionSystem`, end-game presentation, or the other two condition
types' semantics.

## Approach

### 1. Decrease shipped comparison scores + calibration constant

In `Assets/Configs/game_settings.json`, multiply each of the nine `endGameComparisons[].score`
values by `0.8` and round to the nearest integer (`Math.Round(x * 0.8, MidpointRounding.AwayFromZero)`),
preserving ascending order:

| id | current | × 0.8 (rounded) |
|---|---|---|
| CommitteeOf300 | 14349 | 11479 |
| TrilateralCommission | 55601 | 44481 |
| BilderbergGroup | 96853 | 77482 |
| DeepState | 138105 | 110484 |
| Reptilians | 179357 | 143486 |
| SkullAndBones | 220609 | 176487 |
| NewWorldOrder | 261861 | 209489 |
| KnightsTemplar | 303113 | 242490 |
| BohemianGrove | 344365 | 275492 |

In `src/Game.Tests/EndGameThresholdFormulaTests.cs`, change `ShippedCalibrationMaximum` (line 15)
from `286971.0094511145` to `286971.0094511145 * 0.8 = 229576.8075608916`. Do not touch `Factor(i)`
or the rounding policy. `shipped_end_game_comparisons_match_formula_against_calibration_maximum`
must pass unchanged against the new config values and constant.

No change to `src/Game.Main/EndGameComparisonProjector.cs` or
`Assets/Scripts/Unity/UI/EndGameWindowView.cs` — both consume `EndGameComparisons` as-is.

### 2. `score_goal` condition type

Add `ScoreGoal` to `src/Game.Configs/CompletionConditionType.cs`'s enum and
`"score_goal" => CompletionConditionType.ScoreGoal` to `CompletionConditionTypeParser.TryParse`,
alongside the existing two cases.

Add `src/Game.Systems/ScoreGoalCondition.cs` (pattern per `TotalControlCondition`/
`FullControlCondition`):

```csharp
using System;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class ScoreGoalCondition : ICompletionCondition {
		readonly double _goal;

		public ScoreGoalCondition(double goal) {
			if (double.IsNaN(goal) || double.IsInfinity(goal) || goal <= 0.0) {
				throw new ArgumentOutOfRangeException(nameof(goal), goal,
					"Score-goal completion threshold must be a positive finite value.");
			}
			_goal = goal;
		}

		public bool IsMet(CompletionConditionContext context) {
			double score = ResourceQuery.GetValue(context.World, context.OrganizationId, ResourceDefinitions.OrgScore);
			return score >= _goal;
		}
	}
}
```

No `AvailableCountryIds`/`MaxControlPool` involvement — the score is a plain org-owned `Resource`
(`org_score`, collector-driven, not a standalone component; per
`.claude/rules/unity/ecs_patterns.md`'s composition/`[Savable]` notes, no new component is added).

Wire it into `src/Game.Systems/CompletionConditionFactory.cs`'s `Create` switch:

```csharp
case CompletionConditionType.ScoreGoal:
	return CreateScoreGoal(config.Value, path);
```

```csharp
static ICompletionCondition CreateScoreGoal(double value, string path) {
	try {
		return new ScoreGoalCondition(value);
	} catch (ArgumentOutOfRangeException exception) {
		throw new ArgumentException($"Invalid completion condition at '{path}': {exception.Message}", exception);
	}
}
```

### 3. Config wiring

In `src/Game.Configs/GameSettings.cs`, add the third `any` member to the default
`CompletionCondition` (lines 36-42):

```csharp
public CompletionConditionConfig CompletionCondition { get; set; } = new CompletionConditionConfig {
	Type = "any",
	Members = new List<CompletionConditionConfig> {
		new CompletionConditionConfig { Type = "total_control", Value = 0.8 },
		new CompletionConditionConfig { Type = "full_control_countries", Value = 15 },
		new CompletionConditionConfig { Type = "score_goal", Value = 275592 }
	}
};
```

Add the matching member to `Assets/Configs/game_settings.json`'s `completionCondition.members`
array (after decreasing the comparison scores in Step 1, so the `275592` value is verifiably
`max(decreasedScores) + 100`):

```json
{ "type": "score_goal", "value": 275592 }
```

No change needed to `src/Game.Main/GameLogic.cs` — its existing
`CompletionConditionFactory.Create(settings.CompletionCondition, MaxControlPool)` call (line 95)
already builds whatever tree the config describes.

### 4. Win-conditions hint row

In `src/Game.Main/VisualState.cs`, add `ScoreGoal` to `WinConditionHintKind` (line 880-883):

```csharp
public enum WinConditionHintKind {
	TotalControl,
	FullControlCountries,
	ScoreGoal
}
```

`WinConditionHintRowState` needs no shape change — its existing `Value` carries the numeric goal
(`275592`); `AvailableCountryCount` is simply unused for this kind, same as it's meaningless for
`TotalControl` today.

In `src/Game.Main/WinConditionHintProjector.cs`'s `Flatten` switch, add:

```csharp
case CompletionConditionType.ScoreGoal:
	rows.Add(new WinConditionHintRowState(WinConditionHintKind.ScoreGoal, condition.Value, availableCountryCount));
	break;
```

In `Assets/Scripts/Unity/UI/SelectOrgDocument.cs`'s `FormatGoalHintRow` switch, add a case
formatting the goal value as an integer:

```csharp
case WinConditionHintKind.ScoreGoal:
	return string.Format(
		_localization.Get("select_org.win_conditions.score_goal"),
		((int)row.Value).ToString(CultureInfo.InvariantCulture));
```

This is pure C# projector logic (`WinConditionHintProjector`) plus one `switch` case in an
already-existing Unity binding script (`SelectOrgDocument`) — no UXML/USS change. The existing
`goal-hint-rows` container in `Assets/UI/Modal/SelectCountry/SelectCountry.uxml` already renders
one `Label` per `WinConditionHintRowState` generically (see `RefreshGoalHint`); a new row kind
needs no new visual element, template, or Editor-side wiring.

### 5. Localization

Add to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, alongside the
existing `select_org.win_conditions.*` keys:

- `select_org.win_conditions.score_goal` — e.g. `"Reach a total score of {0}"` (English). Use the
  `localization` skill for the real Russian translation (batch this one key through the skill's
  Haiku subagent call; do not hand-write a placeholder).

Follow `.claude/rules/unity/localization.md`: numeric goal formatted as `{0}` data inside the
template, never concatenated.

### 6. Tests

See **Tests** section below. Extend `src/Game.Tests/EndGameThresholdFormulaTests.cs` for the
decreased-scores/calibration-constant assertion (already covered by the existing
`shipped_end_game_comparisons_match_formula_against_calibration_maximum` test once the constant is
updated in Step 1 — no new test needed there beyond updating the constant) and add a new
`src/Game.Tests/ScoreGoalConditionTests.cs` plus `score_goal` cases in
`src/Game.Tests/CompletionConditionTests.cs`-style factory/config coverage.

### 7. Build + verify

Run `dotnet test src/GlobalStrategy.Core.sln` (via the `dotnet-test` skill), then
`dotnet build src/GlobalStrategy.Core.sln -c Release` to refresh `Assets/Plugins/Core/`. Let Unity
import the changed `en.asset`/`ru.asset`/`SelectOrgDocument.cs`, `refresh_unity`, and
`read_console(types=["error"])`.

## Tests

Core (`src/Game.Tests`), all pure C#, run via `dotnet test src/GlobalStrategy.Core.sln`:

- **`EndGameThresholdFormulaTests`** (existing file, updated constant): re-verify
  `shipped_end_game_comparisons_match_formula_against_calibration_maximum` passes against the
  decreased `endGameComparisons` values and the scaled `ShippedCalibrationMaximum`; add a small
  assertion (either in this file or a new one) that
  `settings.EndGameComparisons.Max(e => e.Score) + 100 == 275592`, pinning the derived goal
  constant to the decreased scores mechanically rather than only by reviewer inspection.
- **`ScoreGoalConditionTests`** (new file, mirroring `CompletionConditionTests`'s
  `TotalControlCondition`/`FullControlCondition` coverage style):
  - Score strictly below the goal → `IsMet` is `false`.
  - Score exactly equal to the goal → `IsMet` is `true` (equality counts).
  - Score above the goal → `IsMet` is `true`.
  - Zero/absent `org_score` resource for the organization → `IsMet` is `false` (no resource entity
    at all, per `ResourceQuery.GetValue`'s documented zero-default fallback).
  - Constructor rejects non-positive, `NaN`, and infinite goal values with
    `ArgumentOutOfRangeException`, mirroring `TotalControlCondition`'s/`FullControlCondition`'s
    existing constructor-validation tests.
- **`CompletionConditionTests`** (existing file, extended): `CompletionConditionFactory.Create`
  builds a working `ScoreGoalCondition` from a `{ "type": "score_goal", "value": N }` node; a
  three-member `any` (`total_control`, `full_control_countries`, `score_goal`) is met when *any*
  one qualifies, including the case where only the score goal is met while control-based
  thresholds are not; an unparseable/unknown type still fails fast via the existing
  `CompletionConditionTypeParser.TryParse` guard (already covered, just confirm `score_goal` is
  recognized and nothing else regresses).
- **`WinConditionHintProjectorTests`** (existing file, extended): a `score_goal` leaf produces a
  `WinConditionHintKind.ScoreGoal` row carrying its configured `Value`; a three-leaf `any`
  (`total_control` + `full_control_countries` + `score_goal`) flattens to three rows in
  configuration order with `isAlternativeGroup == true`.
- Existing `GameCompletionSystemTests`/`GameCompletionLogicTests` must continue passing
  unmodified — `GameCompletionSystem.Update` needs no change and this feature must not alter
  tie-break order, winner/loser assignment, or the two existing condition types' semantics.

Unity verification (see **User Steps** below): confirm the new hint row renders correctly in
English and Russian on the organization-selection screen, and that the decreased comparison scores
still render correctly ranked in the end-game window.

## Section 1 — Agent Steps

- [ ] Multiply the nine `Assets/Configs/game_settings.json` `endGameComparisons[].score` values by `0.8` (rounded away-from-zero) per the table above, preserving ascending order.
- [ ] Update `ShippedCalibrationMaximum` in `src/Game.Tests/EndGameThresholdFormulaTests.cs` (line 15) from `286971.0094511145` to `229576.8075608916`; add the `max(decreasedScores) + 100 == 275592` pinning assertion.
- [ ] Add `ScoreGoal` to `src/Game.Configs/CompletionConditionType.cs`'s enum and `"score_goal"` to `CompletionConditionTypeParser.TryParse`.
- [ ] Add `src/Game.Systems/ScoreGoalCondition.cs` implementing `ICompletionCondition.IsMet` via `ResourceQuery.GetValue(context.World, context.OrganizationId, ResourceDefinitions.OrgScore) >= goal`, with constructor validation mirroring `TotalControlCondition`/`FullControlCondition`.
- [ ] Wire `CompletionConditionType.ScoreGoal` into `src/Game.Systems/CompletionConditionFactory.cs`'s `Create` switch via a new `CreateScoreGoal` helper.
- [ ] Add the third `any` member (`score_goal`, value `275592`) to `GameSettings.CompletionCondition`'s default in `src/Game.Configs/GameSettings.cs` and to `Assets/Configs/game_settings.json`'s `completionCondition.members`.
- [ ] Add `ScoreGoal` to `WinConditionHintKind` in `src/Game.Main/VisualState.cs` and the matching `Flatten` branch in `src/Game.Main/WinConditionHintProjector.cs`.
- [ ] Add the `WinConditionHintKind.ScoreGoal` case to `FormatGoalHintRow` in `Assets/Scripts/Unity/UI/SelectOrgDocument.cs`.
- [ ] Add `select_org.win_conditions.score_goal` to `Assets/Localization/en.asset`, then use the `localization` skill to produce and add the real Russian translation to `Assets/Localization/ru.asset`.
- [ ] Add `src/Game.Tests/ScoreGoalConditionTests.cs` (below/equal/above threshold, absent-resource zero-default, constructor validation) and extend `src/Game.Tests/CompletionConditionTests.cs` / `src/Game.Tests/WinConditionHintProjectorTests.cs` per the **Tests** section.
- [ ] Run `dotnet test src/GlobalStrategy.Core.sln` (via `dotnet-test` skill), then `dotnet build src/GlobalStrategy.Core.sln -c Release`; refresh Unity and confirm a clean console via `read_console(types=["error"])`.

## Section 2 — User Steps

### 1. Visual QA — win-conditions hint panel

In the organization-selection scene, confirm the new score-goal row appears in the `Win
conditions` panel (English), reads a sensible integer goal value, and sits correctly alongside the
existing `total_control`/`full_control_countries` rows with the "any one of the following" cue
still showing (three rows now, not two).

### 2. Visual QA — Russian locale

Switch locale to Russian and repeat the check above; confirm no raw locale key appears in place of
the new row's text.

### 3. End-game comparison block re-check

Trigger a game completion (any win path) and confirm the end-game comparison block still ranks
the player correctly among the nine predefined entries at their new, lower decreased scores — this
is a regression check on already-implemented projector code, not new UI, but the actual displayed
numbers changed and are worth eyeballing once.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering (Unity 6 + URP only).** Not touched — no shaders, materials, or camera-stack changes.
- **Game Logic (ECS in `src/`).** `ScoreGoalCondition` is a plain, stateless C# class in
  `src/Game.Systems` implementing the existing `ICompletionCondition` contract, evaluated the same
  way `TotalControlCondition`/`FullControlCondition` already are — no MonoBehaviour holds game
  rules, and `GameCompletionSystem.Update`'s generic per-participant evaluation/winner-selection
  logic is untouched. The score value itself is read via the existing `ResourceQuery.GetValue`
  pure query against the already-existing `org_score` `Resource`, not a new component.
- **Dependency Injection (VContainer only).** No new service, `FindObjectOfType`, or static mutable
  singleton is introduced; `WinConditionHintProjector`/`SelectOrgDocument` continue to be driven
  through the existing `SelectOrgLogic`/`VisualState` wiring with no new injection point.
- **UI (UI Toolkit only).** The one UI-facing change is a new `switch` case in
  `SelectOrgDocument.FormatGoalHintRow` plus new locale keys — the existing `goal-hint-rows`
  container in `SelectCountry.uxml` already renders rows generically from
  `WinConditionHintState.Rows`, so no UXML/USS edit or Editor-side visual-element binding is
  required for this feature.
- **Planning Discipline (plan before implement).** This plan is the required approved-plan artifact
  before any code/asset change begins.
- **Specification Discipline (spec before plan).**
  `Docs/Specs/26_08_02_13_score-count-goal/spec.md` already exists and was approved before this
  plan was written.
- **File Organisation.** This plan lives at
  `Docs/Specs/26_08_02_13_score-count-goal/plan.md`, beside its spec, per convention.
- **Assembly Structure (one `.asmdef` per feature folder).** All new/edited `src/` types stay
  within the existing `Game.Configs`, `Game.Systems`, and `Game.Main` projects; the one Unity-side
  edit stays in `Assets/Scripts/Unity/UI/`'s existing `.asmdef`-covered folder. No new feature
  folder or assembly is introduced.
- **C# Code Style.** All new/edited code uses tabs, same-line braces, `_`-prefixed private fields,
  no redundant access modifiers, and fail-fast, contextual `ArgumentOutOfRangeException`/
  `ArgumentException` validation matching the sibling condition types' existing pattern.

Use the implement skill to start working on the plan or request changes.
