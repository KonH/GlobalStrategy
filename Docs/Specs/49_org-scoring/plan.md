# Plan: Org Scoring

## Spec

Source: `Docs/Specs/49_org-scoring/spec.md`.

**Intent.** Give each existing organization a numeric score — `coefficient * sum(ControlEffect.Value across all countries where ControlEffect.OrgId == that org's id)` — recomputed once per in-game month (same `isMonthBoundary` cadence already used by `ControlSystem`/`ResourceSystem`), plus forced recomputes at init and on load. Control-only; gold is explicitly excluded from the formula. Pure `src/` ECS feature; no UI, no MonoBehaviour, no new Unity assembly.

**Ordering note.** This feature is a prerequisite for the future multi-org/bot-evaluation work, not the other way around — you cannot judge bot efficiency without a score to compare orgs by. It is deliberately self-contained: it computes a score for however many `Organization` entities exist today (currently exactly one), and requires zero changes once a later multi-org feature creates more, since its aggregation already iterates *all* `Organization` entities rather than assuming a fixed count.

**Dependency.** Depends only on `Docs/Plans/48_score-component-composition.md` (technical refactor, no external dependency of its own), which introduces the shared, non-`[Savable]` `src/Game.Components/Score.cs` (`{ double Value }`) component and the "compose a derived value directly onto its subject entity" pattern (`.claude/rules/unity/ecs_patterns.md`'s "Composition over parallel lookup entities for derived per-entity state"), first applied there to `Country + Score`. This plan reuses that exact same `Score` component, composed onto `Organization` entities (`Organization + Score`) instead of inventing a second `OrgScore { OrgId, Value }` type.

**This plan has NO dependency on multi-org support.** An earlier draft of this plan depended on a not-yet-merged multi-org spec's `OrgMetrics.GetTotalControl` helper and multi-org `InitSystem` loop — that dependency has been removed. `OrgScoreSystem` performs its own `ControlEffect` summation directly (the same straightforward archetype scan `ControlSystem.Update` already does for its own per-country grouping, just summed per-org instead), and iterates whatever `Organization` entities currently exist in the world — one today, however many once a future multi-org feature creates more, with zero code changes required on this side either way. Likewise, all "headless runner JSON output" integration has been removed from this plan's scope — a future multi-org feature will consume `OrgScoreSystem.GetScore(world, orgId)` directly when it needs a score for its own results, per this spec's explicit acceptance criterion that this feature's public API is the intended integration point.

This plan also follows the pattern established by the now-shipped `Docs/Specs/47_country-scoring/spec.md`/`CountryScoreSystem` (PR #16): a static system with a month-boundary-gated `Update` plus a forced ungated `Recompute` invoked at init and on load; a global coefficient in `game_settings.json`; and a `GetScore`-style query API.

**Key acceptance criteria (design targets):**
- Score = `coefficient * sum(ControlEffect.Value across all countries where ControlEffect.OrgId == that org's id)`, recomputed at each month boundary (`isMonthBoundary`, same gating pattern as `ResourceSystem`/`ControlSystem`).
- An org with zero `ControlEffect` entities, or all of value `0`, has score `0`, never an error or missing value.
- A single month-boundary-gated `OrgScoreSystem` is the only recalculation mechanism.
- Skipping multiple month boundaries in one `Update()` call still recomputes exactly once, from current control at call time.
- Control changes mid-month do not move any org's score until the next month-boundary recompute.
- Works correctly for however many `Organization` entities exist — one org's control change affects only that org's score, verified with two-or-more orgs seeded directly in a unit test (no multi-org game-init feature required for this to be testable).
- Score is exposed per-org in `VisualState` via a new slice (`OrgScoreState`), following the `Set(dictionary)`-rebuilt-every-tick idiom already used by `CountryScoreState`. No UI/leaderboard consumer is implemented.
- The scoring coefficient is a single global tunable value (not per-org), adjustable via a new `game_settings.json` field kept distinct from `countryScoreCoefficient`.
- Score is runtime-only, not persisted (`[Savable]` omitted, per `ecs_patterns.md`'s derived-component convention), and is recomputed immediately at init/load.
- `OrgScoreSystem.GetScore(world, orgId)` is the intended integration point for any future consumer (multi-org eval, UI, etc.) — no changes to this feature should be needed merely because a consumer is later built on top of it.

**Out of scope:** any change to `ControlEffect`/`ControlSystem`/`ResourceSystem` or how control/gold are granted; any consumer of the score (UI, leaderboard, win/loss conditions, bot/AI decisions, or a headless simulation runner and its results JSON); folding gold or any other metric into the formula; multi-org world initialization, seeded RNG, or a headless console runner (a future feature's job, which will consume this one); per-country score breakdown per org; historical score/timeline tracking.

## Goal

Add an `OrgScoreSystem` that aggregates each existing org's total control directly from `ControlEffect` entities into a single `coefficient * totalControl` value per org, composed directly onto that org's `Organization` entity as a shared `Score` component (per `Docs/Plans/48_score-component-composition.md` — no separate `OrgScore` entity/type), recomputed on the same month-boundary cadence already used by `ResourceSystem`/`ControlSystem`, plus forced ungated recomputes at init (`InitSystem.Run`) and on load (`GameLogic.LoadState`). Expose the result through a new `VisualState.OrgScore` slice, rebuilt unconditionally every tick by `VisualStateConverter` (bounded by however many `Organization` entities exist — currently one). No consumer, no UI, no persistence, no headless-runner integration — this plan only computes and exposes the value via `VisualState` and the public `GetScore` API, for a future feature to consume.

## Approach

- **No new score component.** Per `Docs/Plans/48_score-component-composition.md`, `src/Game.Components/Score.cs` (`{ double Value }`, not `[Savable]`) already exists by the time this plan is implemented. `OrgScoreSystem` composes it directly onto the existing `Organization` entity for each org present in the world — `Organization + Score` is the "org score context," exactly mirroring `Country + Score` for country score. There is no `OrgId` field anywhere in the score storage itself; the co-located `Organization.OrganizationId` on the same entity is the identity.

- **New system** `src/Game.Systems/OrgScoreSystem.cs`, static, mirroring the (post-refactor) `CountryScoreSystem`'s archetype-iteration style:
  - `Update(World world, DateTime previousTime, DateTime currentTime, double coefficient)` — computes `isMonthBoundary` exactly like `ControlSystem.Update` (`previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year`), early-returns if not crossed, else calls `Recompute`.
  - `Recompute(World world, double coefficient)` — the forced, ungated entry point (needed by `InitSystem.Run` and `GameLogic.LoadState`, exactly as `CountryScoreSystem.Recompute` is needed by the same two call sites). Aggregation:
    ```csharp
    public static void Recompute(World world, double coefficient) {
    	// Sum ControlEffect.Value per OrgId directly — this plan has no dependency on a
    	// multi-org "OrgMetrics" helper, so it does its own summation here, the same
    	// straightforward archetype scan ControlSystem.Update already does for its
    	// per-country grouping (just summed per-org instead of per-country).
    	var controlByOrgId = new Dictionary<string, int>();
    	int[] controlRequired = { TypeId<ControlEffect>.Value };
    	foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
    		ControlEffect[] effects = arch.GetColumn<ControlEffect>();
    		int count = arch.Count;
    		for (int i = 0; i < count; i++) {
    			if (!controlByOrgId.TryGetValue(effects[i].OrgId, out int existing)) {
    				existing = 0;
    			}
    			controlByOrgId[effects[i].OrgId] = existing + effects[i].Value;
    		}
    	}

    	// Collect (entity, orgId) pairs first — see the correctness note below on why
    	// world.Add cannot happen inside the GetMatchingArchetypes enumeration itself.
    	var orgEntities = new List<(int entity, string orgId)>();
    	int[] orgRequired = { TypeId<Organization>.Value };
    	foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
    		Organization[] orgs = arch.GetColumn<Organization>();
    		int count = arch.Count;
    		for (int i = 0; i < count; i++) {
    			orgEntities.Add((arch.Entities[i], orgs[i].OrganizationId));
    		}
    	}

    	foreach (var (entity, orgId) in orgEntities) {
    		controlByOrgId.TryGetValue(orgId, out int totalControl);
    		double value = coefficient * totalControl;
    		if (world.Has<Score>(entity)) {
    			world.Get<Score>(entity).Value = value;
    		} else {
    			world.Add(entity, new Score { Value = value });
    		}
    	}
    }
    ```
    An org with no matching `ControlEffect` entries still gets `Score { Value = 0 }` composed onto it — satisfies the "zero control → score 0, not missing" criterion. The collect-then-mutate two-pass shape for the `Organization` scan (rather than adding `Score` inline inside that `foreach`) is required for the same reason `InitSystem.DiscoverInitialCountries` and the refactored `CountryScoreSystem.Recompute` both do it: calling `world.Add` on an entity's *first* `Score` attachment triggers an archetype move, which would mutate the archetype dictionary mid-enumeration and throw `InvalidOperationException` if done inline.
  - `GetScore(IReadOnlyWorld world, string orgId)` — requires `{ TypeId<Organization>.Value, TypeId<Score>.Value }`, scans for the matching `OrganizationId`, returns `Value`, else `0` if no match (mirrors the post-refactor `CountryScoreSystem.GetScore` shape exactly).
  - **Design decision — this system owns its own control-summation, unlike an earlier draft that planned to reuse a multi-org `OrgMetrics.GetTotalControl` helper.** That helper does not exist and this plan no longer depends on the spec that would introduce it. `OrgScoreSystem`'s inline summation is small (mirrors `ControlSystem.Update`'s existing per-country grouping loop almost exactly) and self-contained. If a future multi-org feature later introduces a shared `OrgMetrics` class for its own purposes (e.g. headless-runner reporting), it is that feature's choice whether to refactor `OrgScoreSystem` to call it instead — not a requirement of this plan.

- **Config**: add `public double OrgScoreCoefficient { get; set; } = 1.0;` to `src/Game.Configs/GameSettings.cs` and `"orgScoreCoefficient": 1.0` to `Assets/Configs/game_settings.json`. **`1.0` is a placeholder pending game-design balancing**, same caveat as `countryScoreCoefficient` — kept as a fully separate field so the two scoring scales can be tuned independently, per the spec's explicit acceptance criterion.

- **Wiring into `GameLogic`** (`src/Game.Main/GameLogic.cs`):
  - Constructor: add `readonly double _orgScoreCoefficient;` field, set from `settings.OrgScoreCoefficient` alongside the existing `_countryScoreCoefficient = settings.CountryScoreCoefficient;` line.
  - `Update(float deltaTime)`: add `OrgScoreSystem.Update(_world, _previousTime, currentTime, _orgScoreCoefficient);` **at the end of the systems-processing block, immediately before `_commandAccessor.Clear();`** — i.e. after the `ReadChangeControlCommand`/`ApplyChangeControl` loop and after `CreateActionEffectSystem.Update(...)`, not immediately after `ControlSystem.Update`. **Placement decision:** `ControlEffect` is mutated twice more within the same `Update()` call *after* `ControlSystem.Update` runs: (1) the `ReadChangeControlCommand`/`ApplyChangeControl` command loop, and (2) `CreateActionEffectSystem.Update`, which creates new `ControlEffect` entities whenever a played card resolves a `ControlChangeEffectParams` effect — the actual in-game mechanism for gaining control. Placing `OrgScoreSystem.Update` right after `ControlSystem.Update` would mean a control-granting card resolved on the same frame as a month boundary is silently excluded from that month's score for a full month. Placing the call at the end of the block, after every system/command loop that can create or mutate `ControlEffect`, guarantees the recompute (when it fires) sees fully-settled control state for the tick.
  - `LoadState(string saveName)`: after `RefreshSingletonEntities();`, add `OrgScoreSystem.Recompute(_world, _orgScoreCoefficient);` — a forced, ungated recompute so a freshly loaded save has correct scores immediately, not `0`/stale until the next boundary (same fix `CountryScoreSystem`'s `LoadState` wiring already applies).

- **Wiring into `InitSystem`** (`src/Game.Main/InitSystem.cs`): add `OrgScoreSystem.Recompute(world, settings.OrgScoreCoefficient);` near the end of `Run`, immediately alongside the existing `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` call (both go right before the final `world.Add(initEntity, new IsInitialized());`). `settings` is already loaded earlier in `Run`, so `settings.OrgScoreCoefficient` is available with no extra config load. This works against the current `InitSystem.Run`, which creates exactly one `Organization` entity today — `OrgScoreSystem.Recompute` iterates whatever `Organization` entities exist, so it needs no change if/when a future multi-org feature creates more.

- **`VisualState` exposure** (`src/Game.Main/VisualState.cs`): add `OrgScoreState : INotifyPropertyChanged` (idiom of the existing `CountryScoreState`) exposing `IReadOnlyDictionary<string, double> ScoreByOrgId` and `Set(IReadOnlyDictionary<string, double> scoreByOrgId)` firing `PropertyChanged`. Add `public OrgScoreState OrgScore { get; } = new OrgScoreState();` to the `VisualState` aggregate class. **Design decision — expose all existing orgs, not just the view/player org.** `VisualStateConverter.Update(...)`'s existing signature takes a single `orgEntity` parameter (the player/view org), and existing `Update*(world, orgEntity)` methods are scoped to that one org's perspective. `UpdateOrgScore` must **not** be keyed off `orgEntity` — it iterates all entities carrying both `Organization` and `Score` directly, the same unconditional-full-rebuild approach `UpdateCountryScore` uses.

- **`VisualStateConverter` population** (`src/Game.Main/VisualStateConverter.cs`): add
  ```csharp
  void UpdateOrgScore(IReadOnlyWorld world) {
  	var scoreByOrgId = new Dictionary<string, double>();
  	int[] required = { TypeId<Organization>.Value, TypeId<Score>.Value };
  	foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
  		Organization[] orgs = arch.GetColumn<Organization>();
  		Score[] scores = arch.GetColumn<Score>();
  		int count = arch.Count;
  		for (int i = 0; i < count; i++) {
  			scoreByOrgId[orgs[i].OrganizationId] = scores[i].Value;
  		}
  	}
  	_state.OrgScore.Set(scoreByOrgId);
  }
  ```
  Call it from `Update(...)`'s existing sequence, alongside `UpdateOrgMap(world, orgEntity)` and `UpdateCountryScore(world)` (no `orgEntity` argument needed for `UpdateOrgScore` itself, per the decision above). **Deliberately no dirty-check/version-counter machinery** — `Score`-on-`Organization` has as many entries as there are orgs in the world (currently one), so rebuilding unconditionally every tick is negligible cost, matching `UpdateOrgMap`'s and `UpdateCountryScore`'s existing no-dirty-check style.

- **No headless-runner/JSON integration in this plan.** A future multi-org feature will call `OrgScoreSystem.GetScore(world, orgId)` directly when it builds its own results output — that is entirely that future feature's implementation work, not this plan's.

- **No new `.asmdef`, no VContainer change, no UI change.** `Assets/Plugins/Core/` picks up the new types automatically on the next `dotnet build src/GlobalStrategy.Core.sln -c Release`.

## Steps

### Agent Steps

- [ ] **Confirm the score-component prerequisite has landed** — verify `src/Game.Components/Score.cs` exists and `CountryScoreSystem` has been refactored to compose it onto `Country` entities, per `Docs/Plans/48_score-component-composition.md`. If missing, stop.

- [ ] **Add the `OrgScoreSystem`** — Create `src/Game.Systems/OrgScoreSystem.cs` with `Update(World, DateTime previousTime, DateTime currentTime, double coefficient)` (month-boundary gate, delegates to `Recompute`), `Recompute(World, double coefficient)` (the self-contained control-summation + collect-then-mutate composition shown in the Approach section), and `GetScore(IReadOnlyWorld, string orgId)` (requires `Organization + Score`, `0` if absent). No new component file — this system consumes the shared `Score` component.

- [ ] **Add the config coefficient** — In `src/Game.Configs/GameSettings.cs`, add `public double OrgScoreCoefficient { get; set; } = 1.0;`. In `Assets/Configs/game_settings.json`, add `"orgScoreCoefficient": 1.0` alongside the existing keys (camelCase per `plugins.md`'s JSON convention), distinct from `countryScoreCoefficient`.

- [ ] **Seed initial score at init** — In `src/Game.Main/InitSystem.cs`'s `Run`, add `OrgScoreSystem.Recompute(world, settings.OrgScoreCoefficient);` alongside the existing `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` call, before the final `world.Add(initEntity, new IsInitialized());`.

- [ ] **Wire the monthly recompute into `GameLogic.Update`** — In `src/Game.Main/GameLogic.cs`, add `readonly double _orgScoreCoefficient;` set from `settings.OrgScoreCoefficient` in the constructor (alongside `_countryScoreCoefficient`). In `Update(float deltaTime)`, add `OrgScoreSystem.Update(_world, _previousTime, currentTime, _orgScoreCoefficient);` at the **end** of the systems-processing block, immediately before `_commandAccessor.Clear();` (NOT immediately after `ControlSystem.Update`, since `ControlEffect` is mutated later in the same tick by the `ChangeControlCommand` loop and `CreateActionEffectSystem.Update`).

- [ ] **Force a recompute on load** — In `src/Game.Main/GameLogic.cs`'s `LoadState(string saveName)`, add `OrgScoreSystem.Recompute(_world, _orgScoreCoefficient);` directly after the existing `RefreshSingletonEntities();`/`CountryScoreSystem.Recompute(...)` calls.

- [ ] **Expose score via `VisualState`** — In `src/Game.Main/VisualState.cs`, add `OrgScoreState : INotifyPropertyChanged` with `IReadOnlyDictionary<string, double> ScoreByOrgId` and `Set(...)`, and add `public OrgScoreState OrgScore { get; } = new OrgScoreState();` to the `VisualState` class.

- [ ] **Populate score state every tick** — In `src/Game.Main/VisualStateConverter.cs`, add `UpdateOrgScore(IReadOnlyWorld world)` as shown in the Approach section, and call it from `Update(...)`'s existing sequence next to `UpdateOrgMap(world, orgEntity)`/`UpdateCountryScore(world)`. No version-counter/dirty-check.

- [ ] **Add/extend tests** — Implement the Tests section below.

- [ ] **Rebuild the Core DLLs** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up `OrgScoreSystem`, the updated `GameSettings`, and the `GameLogic`/`InitSystem`/`VisualState`/`VisualStateConverter` changes.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script, so the only expected effect is the updated `Assets/Plugins/Core/*.dll` files and the new `orgScoreCoefficient` key in `Assets/Configs/game_settings.json` being picked up cleanly.

### 2. Sanity-check initial scores in Play mode

Enter Play mode. Using Unity MCP (or a temporary debug read), confirm the (currently single) org's `OrgScoreSystem.GetScore` (or the equivalent value visible via `VisualState.OrgScore.ScoreByOrgId` if inspected through a debugger/log) is present and correct immediately at tick one — proving the `InitSystem.Run` forced recompute worked rather than waiting for the first month boundary.

### 3. Verify month-boundary recompute timing

Advance in-game time (e.g. via a debug time-multiplier or fast-forward) across a month boundary and confirm scores update only at the boundary, not continuously within a month, by checking values immediately before and after the boundary tick.

### 4. Verify control-change deferral

Trigger a control change mid-month (e.g. via whatever mechanism grants `ControlEffect` at the time this is implemented) and confirm the score does not change until the next month-boundary tick.

### 5. Verify save/load recompute

Save the game, reload it, and confirm scores are immediately correct (matching the control state at the moment of save) rather than reading `0` or a stale pre-save value until the next month boundary.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case `[Fact]` names; harness pattern in `InitSystemTests.cs`/`ControlSystemTests.cs`/`CountryScoreSystemTests.cs` — month-boundary date constants, `GameLogicContext`/`StaticConfig<T>` building, `MemoryStorage`, `CapturingSerializer`, `BuildLogic`).

- **New `src/Game.Tests/OrgScoreSystemTests.cs`:**
  - `score_computed_from_total_control_at_month_boundary` — an org with `ControlEffect` entries of value 20 and 30 across two countries, coefficient `0.5`, `OrgScoreSystem.Update(world, Jan31, Feb1, 0.5)` → `GetScore(world, orgId) == 25.0`.
  - `org_with_zero_control_has_zero_score` — an org with no `ControlEffect` entities → score `0` after recompute, not missing/error.
  - `score_unchanged_within_same_month` — `Update(world, Jan1, Jan2, coefficient)` (no month boundary crossed) leaves any previously-recomputed score untouched.
  - `control_change_mid_month_does_not_affect_score_until_boundary` — recompute once, mutate a `ControlEffect.Value` directly (or add a new one), then call `Update` with same-month dates → score unchanged until a real month-boundary `Update` call.
  - `multiple_months_skipped_recomputes_once_from_current_state` — `Update(world, Jan15, Mar20, coefficient)` (skips the Feb boundary entirely) still recomputes exactly once, from current control at call time.
  - `multi_org_independence_one_orgs_control_change_does_not_move_another_orgs_score` — two orgs seeded directly in the test world (no multi-org game-init feature needed — `Organization`/`ControlEffect` entities are created directly, the same way `CountryScoreSystemTests.cs` seeds multiple `Country` entities directly); change one org's control, recompute → only that org's score changes, the other org's score is exactly as before.
  - `recompute_is_forced_and_ungated` — calling `Recompute` directly (not through `Update`) applies immediately regardless of any month-boundary condition — this is the entry point `InitSystem`/`LoadState` rely on.
  - `get_score_returns_zero_for_unknown_org` — `GetScore` on an `orgId` with no matching `Organization + Score` entity returns `0`, not an exception.
  - `score_is_composed_onto_the_organization_entity_not_a_separate_entity` — mirrors plan 48's equivalent country-score test: after `Recompute`, assert the entity found via an `Organization`-only query for a given `orgId` is the same entity id returned by an `Organization + Score`-required query for that `orgId`.

- **Extend `src/Game.Tests/InitSystemTests.cs`:** add `org_score_seeded_at_init_from_initial_control` — after the first `GameLogic.Update()` (using the existing `BuildLogic` harness, unmodified — this feature requires no multi-org harness extension since it works with the current single-org `InitSystem`), the org's `OrgScoreSystem.GetScore` reflects `coefficient * sum(seed base ControlEffect for that org)`, confirming scores are correct from tick one rather than waiting for a month boundary.

- **Extend `src/Game.Tests/InitSystemTests.cs`:** add `org_score_recomputed_immediately_after_load`, alongside the existing `CountryScoreSystem`-focused load test (which already builds a `GameLogic` via the `GameLogicContext`/`MemoryStorage`/`CapturingSerializer`/`BuildLogic` harness and calls `logic.LoadState(...)`) — advance state so control differs from initial seed values, save, call `LoadState` on a fresh `GameLogic` instance built from the same harness, and assert `OrgScoreSystem.GetScore(...)` immediately reflects the loaded control state — not `0`, not stale — proving the forced `Recompute` call added to `LoadState` works without relying on a subsequent month boundary.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* `OrgScoreSystem` and the `GameSettings`-adjacent wiring all live in `src/Game.Systems`, `src/Game.Configs`, `src/Game.Main` — no MonoBehaviour, no Unity-side logic. Nothing in this feature touches `Assets/Scripts/Unity/*`.
- *VContainer sole DI.* No new registrations needed — this feature adds no Unity-side consumer; `VisualState`/`GameLogic` are already resolved through the existing container wiring untouched by this plan.
- *UI Toolkit only.* No UI surface is added or modified — score consumption (any future HUD panel/leaderboard/multi-org eval) is explicitly out of scope per the spec.
- *URP only.* No rendering, shader, or material change.
- *One `.asmdef` per feature folder.* Not applicable — this Constitution bullet is scoped to `Assets/Scripts/`; this feature only touches `src/` (`.csproj`-based, no `.asmdef` involved).
- *Planning/Specification discipline.* This plan follows an approved spec (`Docs/Specs/49_org-scoring/spec.md`) per the standard `/specify` → `/plan` sequence, and gates implementation start on exactly one prerequisite: `Docs/Plans/48_score-component-composition.md`. It deliberately has no dependency on any unmerged multi-org spec, since the ordering was corrected so that future multi-org work depends on this feature instead.
- *File organisation.* Plan lives at `Docs/Specs/49_org-scoring/plan.md`, matching its spec's directory — correct index, correct pairing.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching all surrounding files shown in this plan (`ControlSystem.cs`, and the refactored `CountryScoreSystem.cs` precedent).

Use /implement to start working on the plan or request changes.
