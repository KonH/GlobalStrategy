# Plan: Org Scoring

## Spec

Source: `Docs/Specs/49_org-scoring/spec.md`.

**Intent.** Give each existing organization a score — `Σ over countries of (org's control fraction in that country × that country's CountryScore.Value)`, where control fraction is the org's total `ControlEffect.Value` in a country ÷ 100 (the per-country control cap). This is a pure derived query, `OrgScore.GetScore(IReadOnlyWorld world, string orgId)` in `src/Game.Systems` — no ECS component, no `[Savable]`, no `VisualState` exposure, no new config coefficient. Pure `src/` ECS-adjacent feature; no UI, no MonoBehaviour, no Unity assembly change beyond the automatic Core DLL refresh.

**Ordering note.** This feature is a prerequisite for the future multi-org/bot-evaluation work, not the other way around. It is self-contained: it reads whatever `Organization`/`ControlEffect`/`CountryScore` state exists in the world today (currently one org) and requires zero changes once a later multi-org feature creates more, since it is parameterized by `orgId` and reads world state fresh on every call rather than caching anything.

**Design origin.** This formula and query shape (population-weighted `CountryScore`-based org score, deliberately query-only with no ECS component) matches a decision already resolved with the user (2026-07-15) on a sibling branch's "bot feature eval harness" spec, which needs exactly this query as a building block. Extracting it here as its own small, dependency-light feature — rather than only inside that larger spec — is what makes it available as a genuine prerequisite rather than something buried three specs deep behind multi-org and a bot-API layer.

**Dependencies.**
1. `Docs/Specs/47_country-scoring/spec.md`/`CountryScoreSystem` — **already merged** (PR #16, on `main`). This plan reads `CountryScore`'s already-computed values; no changes needed there.
2. `Docs/Specs/48_score-component-composition/plan.md` — the `Country + Score` composition refactor. Recommended to land first (it's small, self-contained, no external dependency) so this plan's single-pass country-score read is written against the final, post-refactor storage shape (`Country + Score`) rather than the pre-refactor `CountryScore { CountryId, Value }` shape and needing a follow-up edit. Not a hard compile-time blocker — the query's implementation is a one-line difference either way — but implementing plan 48 first avoids the rework.

**This plan has NO dependency on multi-org support, a headless runner, or any bot-org infrastructure.** `OrgScore.GetScore` takes an explicit `orgId` and reads world state fresh on every call — it works identically whether there is one `Organization` entity in the world (today) or many (once a future multi-org feature lands).

**Key acceptance criteria (design targets):**
- Score = `Σ over countries of (org's ControlEffect total in that country ÷ 100) × CountryScore.Value for that country`.
- Multiple `ControlEffect` entities for the same org in the same country sum before weighting, not weighted separately.
- Zero control anywhere → `0.0`, never an error.
- Control only in zero-`CountryScore` countries → `0.0` for that contribution, still returns cleanly.
- Single derived, non-`[Savable]` query — no ECS component, no `VisualState`, the only place in the codebase that knows the formula.
- Deterministic — pure function of world state, no hidden mutable state, no randomness, no wall-clock dependency.
- Single-pass country-score read (avoid O(countries) linear scans repeated once per country the org holds control in).
- `OrgScore.GetScore(world, orgId)` is the intended integration point for any future consumer.

**Out of scope:** ECS component/`VisualState`/UI for org score; changes to `ControlEffect`/`ControlSystem`/`CountryScoreSystem`; a second scoring coefficient; any consumer (headless runner/results JSON, eval harness, UI, win/loss conditions, bot/AI decisions); multi-org world init, seeded RNG, headless console runner; bot decision logic/eval harness/parameter search; per-country score breakdown; historical score/timeline tracking.

## Goal

Add `src/Game.Systems/OrgScore.cs`, a static class with one method — `GetScore(IReadOnlyWorld world, string orgId)` — that computes an org's control-weighted, population-derived score on demand from current `ControlEffect` and `CountryScore` state, with no persisted state, no config addition, and no wiring into `GameLogic`, `InitSystem`, or `VisualState`. This is the entire deliverable: a small, pure, directly-callable query that a future multi-org/eval-harness feature will consume without needing to know how it's computed internally.

## Approach

- **New file** `src/Game.Systems/OrgScore.cs`:
  ```csharp
  using System.Collections.Generic;
  using ECS;
  using GS.Game.Components;

  namespace GS.Game.Systems {
  	// Derived query only — no ECS component, nothing [Savable], no VisualState exposure.
  	// Reads already-computed CountryScore values (see CountryScoreSystem, spec 47) plus
  	// the org's ControlEffects, and is the ONLY place in the codebase that knows the
  	// org-score formula. Consumers treat the result as an opaque comparable number.
  	public static class OrgScore {
  		public static double GetScore(IReadOnlyWorld world, string orgId) {
  			// Read every country's score in one pass — avoids calling the linear-scan
  			// CountryScoreSystem.GetScore once per country the org holds control in,
  			// which would be O(countries) per lookup, O(countries^2) overall.
  			var scoreByCountryId = new Dictionary<string, double>();
  			int[] scoreRequired = { TypeId<Country>.Value, TypeId<Score>.Value };
  			foreach (Archetype arch in world.GetMatchingArchetypes(scoreRequired, null)) {
  				Country[] countries = arch.GetColumn<Country>();
  				Score[] scores = arch.GetColumn<Score>();
  				int count = arch.Count;
  				for (int i = 0; i < count; i++) {
  					scoreByCountryId[countries[i].CountryId] = scores[i].Value;
  				}
  			}

  			// Sum this org's ControlEffect.Value per country in one pass.
  			var controlByCountryId = new Dictionary<string, int>();
  			int[] controlRequired = { TypeId<ControlEffect>.Value };
  			foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
  				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
  				int count = arch.Count;
  				for (int i = 0; i < count; i++) {
  					if (effects[i].OrgId != orgId) {
  						continue;
  					}
  					if (!controlByCountryId.TryGetValue(effects[i].CountryId, out int existing)) {
  						existing = 0;
  					}
  					controlByCountryId[effects[i].CountryId] = existing + effects[i].Value;
  				}
  			}

  			double total = 0;
  			foreach (var (countryId, control) in controlByCountryId) {
  				scoreByCountryId.TryGetValue(countryId, out double countryScore);
  				total += (control / 100.0) * countryScore;
  			}
  			return total;
  		}
  	}
  }
  ```
  `100` is the per-country control cap `ControlSystem` already enforces (`ControlSystem.ApplyChangeControl`: `Math.Min(existingValue + delta, 100 - otherOrgsTotal)`) and matches the income-fraction divisor `ControlSystem.Update` already uses (`(orgControl / 100.0) * countryBaseIncome`) — this plan does not introduce a new constant, it reuses the existing cap.

- **Query shape assumes plan 48 has landed** — the `scoreRequired` array queries `{ Country, Score }` (the post-refactor composition shape), not the pre-refactor standalone `CountryScore` component. If plan 48 has not yet landed when this plan is implemented, substitute `int[] scoreRequired = { TypeId<CountryScore>.Value };` and read the `CountryScore[]` column's `CountryId`/`Value` fields directly instead — a one-line change, not a redesign. The implementing agent's first step (below) confirms which shape is live before writing the query.

- **No config, no `GameSettings` change.** The only tunable in this formula is `countryScoreCoefficient` (already defined, already baked into `CountryScore.Value` by the time this query reads it) — org score does not introduce a second coefficient.

- **No wiring into `GameLogic`, `InitSystem`, or `VisualState`.** Unlike `CountryScoreSystem` (which maintains cached, amortized state recomputed at month boundaries), `OrgScore.GetScore` computes fresh from current world state on every call — there is nothing to seed at init, nothing to recompute on load, and no month-boundary gating, because there is no cached state to go stale. A caller invokes `OrgScore.GetScore(world, orgId)` whenever it needs the value, and always gets a value consistent with the world's current state.

- **No new `.asmdef`, no VContainer change, no UI change.** `Assets/Plugins/Core/` picks up the new type automatically on the next `dotnet build src/GlobalStrategy.Core.sln -c Release`.

## Steps

### Agent Steps

- [x] **Confirm the score-component prerequisite's current shape** — check whether `Docs/Specs/48_score-component-composition/plan.md` has landed (i.e. whether `src/Game.Components/Score.cs` exists and `CountryScoreSystem` composes it onto `Country` entities) or whether `CountryScore { CountryId, Value }` is still the live shape. Use whichever query shape matches — see the Approach section's fallback note. This is the only preflight check this plan needs; there is no multi-org or bot-infrastructure dependency to verify.

- [x] **Add `OrgScore`** — Create `src/Game.Systems/OrgScore.cs` per the Approach section: a single-pass `countryId -> CountryScore.Value` dictionary, a single-pass `countryId -> summed ControlEffect.Value` dictionary for the given `orgId`, then `(control / 100.0) * countryScore` summed across countries, with `0.0` fallbacks for missing entries (never throws).

- [x] **Add tests** — Implement the Tests section below.

- [x] **Rebuild the Core DLLs** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up `OrgScore`.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script and adds no config key, so the only expected effect is the updated `Assets/Plugins/Core/*.dll` files.

### 2. Sanity-check the query against live data

Using Unity MCP (or a temporary debug call), confirm `OrgScore.GetScore(world, orgId)` for the current single org in Play mode returns a plausible value consistent with that org's current control and the countries it holds — e.g. it increases after a country with nonzero `CountryScore` is gained, and is `0.0` at a fresh game start before any control exists.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case `[Fact]` names; harness pattern in `ControlSystemTests.cs`/`CountryScoreSystemTests.cs` — `World` built directly, `Country`/`ControlEffect`/`CountryScore`-or-`Score` entities seeded directly or via `CountryScoreSystem.Recompute`, no `GameLogicContext`/`GameLogic` harness needed since this is a pure query with no init/load lifecycle).

- **New `src/Game.Tests/OrgScoreTests.cs`:**
  - `org_score_is_control_fraction_times_country_score_summed` — an org with 30 control in a country of score 200 and 50 control in a country of score 10 → `OrgScore.GetScore(world, orgId) == 30/100*200 + 50/100*10 == 65.0`.
  - `org_with_no_control_anywhere_scores_zero` — no `ControlEffect` entities for the org anywhere → `0.0`, no throw.
  - `control_in_zero_score_countries_scores_zero` — control only in countries whose score is `0` (or with no country-score entity/entry at all) → `0.0`.
  - `multiple_control_effects_in_one_country_sum_before_weighting` — two separate `ControlEffect` entities (10 + 20) for the org in one country of score 100 → `(10+20)/100*100 == 30.0`, not `10/100*100 + 20/100*100` computed as two separate weighted terms (same numeric result here, but the test should seed the two effects as genuinely separate entities and assert the sum-then-weight order matters conceptually per the spec's explicit criterion).
  - `control_effects_belonging_to_other_orgs_are_excluded` — seed two orgs' `ControlEffect` entities in the same country (e.g. `Org1` with 30 control, `Org2` with 40 control, country score 100), mirroring `ControlSystemTests.multiple_orgs_receive_correct_amounts` (which already proves two orgs' `ControlEffect` entities coexist in a hand-built `World` today with no multi-org feature needed) → `OrgScore.GetScore(world, "Org1") == 30.0`, not `70.0` — asserts the `OrgId` filter, which the formula depends on but no other fact exercises.
  - `get_score_is_pure_and_repeatable` — call `OrgScore.GetScore(world, orgId)` twice in a row on the same unmutated world and assert the two results are equal, then assert no new `ControlEffect`/`Country`/`Score` entities exist afterward — covers the spec's explicit "deterministic... no hidden mutable state" acceptance criterion.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* `OrgScore` is a derived query living in `src/Game.Systems` — no MonoBehaviour, no Unity-side logic, and (deliberately, per the spec) no new ECS component at all.
- *VContainer sole DI.* Not applicable — no new registrations, no Unity-side consumer.
- *UI Toolkit only.* Not applicable — no UI surface added or modified, explicitly out of scope.
- *URP only.* Not applicable — no rendering change.
- *One `.asmdef` per feature folder.* Not applicable — scoped to `src/` (`.csproj`-based, no `.asmdef` involved).
- *Planning/Specification discipline.* This plan follows an approved spec (`Docs/Specs/49_org-scoring/spec.md`) per the standard `/specify` → `/plan` sequence, and gates implementation on exactly one already-merged dependency (spec 47/`CountryScoreSystem`) plus one small, unblocked technical plan (48). It deliberately has no dependency on multi-org, headless-runner, or bot-infrastructure work — the ordering was corrected so that future work of that kind depends on this feature instead.
- *File organisation.* Plan lives at `Docs/Specs/49_org-scoring/plan.md`, matching its spec's directory — correct index, correct pairing.
- *C# style.* Tabs, braces always, `_`-prefixed private members (n/a — no private fields), no redundant access modifiers — matching `ControlSystem.cs`/`CountryScoreSystem.cs`.

Use /implement to start working on the plan or request changes.
