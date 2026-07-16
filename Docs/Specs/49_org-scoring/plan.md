# Plan: Org Scoring

## Spec

Source: `Docs/Specs/49_org-scoring/spec.md`.

**Intent.** Give each participating organization a numeric score — `coefficient * sum(ControlEffect.Value across all countries where ControlEffect.OrgId == that org's id)` — recomputed once per in-game month (same `isMonthBoundary` cadence already used by `ControlSystem`/`ResourceSystem`), plus forced recomputes at init and on load, so the value is always current and queryable by any future consumer (UI, leaderboard, bot/eval harness) without that consumer re-deriving control aggregation itself. Control-only; gold is explicitly excluded from the formula. Pure `src/` ECS feature; no UI, no MonoBehaviour, no new Unity assembly. This is "part 3" (scoring only) of the bot-controlled-organization initiative — the eval harness and bot decision logic are separate, deliberately deferred future features.

**Dependency.** Depends on `Docs/Specs/48_multi-org-headless-simulation/spec.md` (branch `claude/inspiring-hopper-8gh007`, PR #12, **not yet merged**), which is what makes more than one `Organization`/`ControlEffect`/gold entity exist simultaneously in the world. Spec 48 adds `GameLogicContext.RngSeed`/`ParticipatingOrganizationIds`, an `InitSystem` multi-org creation loop (`ResolveParticipatingOrgs`), a new `OrgMetrics.GetTotalControl`/`GetGold` aggregation helper (`src/Game.Systems/OrgMetrics.cs`), and a headless `HeadlessRunner`/`SimulationResult` JSON output (`src/Game.ConsoleRunner/`). None of this exists in the current codebase as of this plan — verified directly: `src/Game.Main/InitSystem.cs` creates exactly one org entity from `context.InitialOrganizationId` (lines 60-84), `src/Game.Main/GameLogicContext.cs` has no `RngSeed`/`ParticipatingOrganizationIds` properties, and there is no `OrgMetrics.cs` under `src/Game.Systems/`. **Implementation of this plan cannot start until spec 48 lands.** This document is written assuming spec 48's shape as described in its own plan (not locally readable — its spec.md/plan.md do not exist in this working copy since the branch is unmerged); the aggregation code below will not compile against `OrgMetrics.GetTotalControl` until the dependency merges.

This plan also follows the pattern established by `Docs/Specs/47_country-scoring/plan.md` (country-scoring, itself still unimplemented in this codebase): a derived, non-`[Savable]` score component; a static system with a month-boundary-gated `Update` plus a forced ungated `Recompute` invoked at init and on load; a global coefficient in `game_settings.json`; and a `GetScore`-style query API.

**Key acceptance criteria (design targets):**
- Score = `coefficient * sum(ControlEffect.Value across all countries where ControlEffect.OrgId == that org's id)`, recomputed at each month boundary (`isMonthBoundary`, same gating pattern as `ResourceSystem`/`ControlSystem`).
- An org with zero `ControlEffect` entities, or all of value `0`, has score `0`, never an error or missing value.
- A single month-boundary-gated `OrgScoreSystem` is the only recalculation mechanism — no separate timing mechanism, and no separate recompute path for the headless case (the headless runner drives the same `GameLogic.Update()` tick loop and already samples once per game month).
- Skipping multiple month boundaries in one `Update()` call still recomputes exactly once, from current control at call time (mirrors `ControlSystem`/`ResourceSystem`/spec 47's existing semantics).
- Control changes mid-month do not move any org's score until the next month-boundary recompute.
- With two or more participating orgs, one org's control change affects only that org's score at the next recalculation — each org's score is computed strictly from `ControlEffect` entities carrying its own `OrgId`.
- Score is exposed per-org in `VisualState` via a new slice (`OrgScoreState`/`OrgScoreEntry` or equivalent), following the `Set(list)`-rebuilt-every-tick idiom already used by `OrgMapState`/`OrgControlEntry`. No UI/leaderboard consumer is implemented.
- Spec 48's headless runner results JSON gets each participating org's derived score added, computed via the same `OrgScoreSystem`/coefficient as the live-game path. **Confirmed user decision:** score is added to **both** the end-of-run `orgs` array **and** every `timeline[].orgs[]` entry (resolving the spec's own `[NEEDS CLARIFICATION]` ambiguity in favor of the timeline-inclusive option) — each org object in both places gets a new `"score": <double>` field.
- The scoring coefficient is a single global tunable value (not per-org), adjustable via a new `game_settings.json` field kept distinct from spec 47's `countryScoreCoefficient` so the two scales tune independently.
- Score is runtime-only, not persisted (`[Savable]` omitted, per `ecs_patterns.md`'s derived-component convention), and is recomputed immediately at init/load so it is never `0`/stale until the next month boundary.

**Out of scope:** any change to `ControlEffect`/`ControlSystem`/`ResourceSystem` or how control/gold are granted; any consumer of the score (UI, leaderboard, win/loss conditions, bot/AI decisions); folding gold or any other metric into the formula; any change to spec 48's headless runner mechanics beyond adding the score field(s); the bot decision logic / eval harness / genetic search spec 48 declared as its own future parts; per-country score breakdown per org.

## Goal

Add an `OrgScore` runtime component and an `OrgScoreSystem` that aggregates each participating org's total control (via the `OrgMetrics.GetTotalControl` primitive spec 48 introduces) into a single `coefficient * totalControl` value per org, recomputed on the same month-boundary cadence already used by `ResourceSystem`/`ControlSystem`, plus forced ungated recomputes at init (`InitSystem.Run`) and on load (`GameLogic.LoadState`) so the value is never stale or zero when it shouldn't be. Expose the result through a new `VisualState.OrgScore` slice, rebuilt unconditionally every tick by `VisualStateConverter` (bounded by the number of participating orgs — currently 2, `Illuminati`/`Masons` — no dirty-check machinery needed), and extend spec 48's headless-runner JSON output with a `score` field on every per-org metric object, in both the end-of-run summary and the per-month timeline. No consumer, no UI, no persistence — this plan only computes and exposes the value.

## Approach

- **New component** `src/Game.Components/OrgScore.cs` — `public struct OrgScore { public string OrgId; public double Value; }`, explicitly **not** `[Savable]`, with a comment following the `ProvinceOwnershipVersion.cs`/spec-47-`CountryScore.cs` convention: fully derivable from `ControlEffect` + the coefficient, so persisting it is wasted space/risk with no benefit.

- **New system** `src/Game.Systems/OrgScoreSystem.cs`, static, mirroring `CountryScoreSystem`'s (spec 47) and `ControlSystem`'s archetype-iteration style:
  - `Update(World world, DateTime previousTime, DateTime currentTime, double coefficient)` — computes `isMonthBoundary` exactly like `ControlSystem.Update` (`previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year`), early-returns if not crossed, else calls `Recompute`.
  - `Recompute(World world, double coefficient)` — the forced, ungated entry point (needed by `InitSystem.Run` and `GameLogic.LoadState`, exactly as spec 47's `CountryScoreSystem.Recompute` is needed by the same two call sites). Aggregation: (1) iterate all `Organization` entities in the world (per spec 48, one per participating org — the system does **not** take an explicit `IReadOnlyList<string> participatingOrgIds>` parameter, it discovers orgs the same way `CountryScoreSystem.Recompute` discovers all `Country` entities directly from the world rather than being handed a list); (2) for each `orgId`, call `OrgMetrics.GetTotalControl(world, orgId)`; (3) destroy all existing `OrgScore` entities (destroy-then-recreate, same style as `CountryScoreSystem.Recompute`/`InitSystem.BuildProximityMap`); (4) create a fresh `OrgScore { OrgId, Value = coefficient * totalControl }` entity per org (an org with `GetTotalControl == 0` still gets an entity with `Value = 0` — satisfies the "zero control → score 0, not missing" criterion).
  - `GetScore(IReadOnlyWorld world, string orgId)` — linear scan returning the matching `Value` or `0` if no entity found (mirrors `CountryScoreSystem.GetScore`/`ProvinceOwnershipSystem.GetOwner`'s shape/fallback).
  - **Design decision — reuse `OrgMetrics.GetTotalControl` rather than reimplementing control summation.** Spec 48's plan already centralizes "sum `ControlEffect.Value` where `OrgId` matches, across all countries" in `OrgMetrics.GetTotalControl(IReadOnlyWorld world, string orgId)`, and spec 49's own Out-of-Scope section states this feature "only aggregates the existing `ControlEffect.Value` per org" — a second, independent summation loop inside `OrgScoreSystem` (duplicating the same archetype scan `OrgMetrics` already does) would risk the two implementations silently diverging (e.g. one picking up a future control-effect filter the other misses) and adds no value over calling the shared helper. `OrgScoreSystem.Recompute` therefore calls `OrgMetrics.GetTotalControl` per org and does no direct `ControlEffect` archetype iteration of its own.

- **Config**: add `public double OrgScoreCoefficient { get; set; } = 1.0;` to `src/Game.Configs/GameSettings.cs` and `"orgScoreCoefficient": 1.0` to `Assets/Configs/game_settings.json`. **`1.0` is a placeholder pending game-design balancing**, same caveat as spec 47's `countryScoreCoefficient` — kept as a fully separate field so the two scoring scales can be tuned independently, per the spec's explicit acceptance criterion.

- **Wiring into `GameLogic`** (`src/Game.Main/GameLogic.cs`):
  - Constructor: add `readonly double _orgScoreCoefficient;` field, set from `settings.OrgScoreCoefficient` alongside the existing `_populationGrowthPercent = settings.PopulationGrowthPercentPerMonth;` line.
  - `Update(float deltaTime)`: add `OrgScoreSystem.Update(_world, _previousTime, currentTime, _orgScoreCoefficient);` **immediately after `ControlSystem.Update(_world, _previousTime, currentTime);`** — i.e. before `OpinionSystem.Update`/`ProvincePopulationGrowthSystem.Update`. **Placement decision:** `OrgScoreSystem` depends only on `ControlEffect` (via `OrgMetrics.GetTotalControl`), not population or opinion, unlike spec 47's `CountryScoreSystem` which depends on province population and is therefore placed after `ProvincePopulationGrowthSystem.Update`. Since control is already final for the tick immediately after `ControlSystem.Update` runs, placing `OrgScoreSystem.Update` directly after it is the tightest-scoped correct placement — no other system between `ControlSystem.Update` and the end of the monthly-systems block can further change `ControlEffect` values within the same tick, so there is no benefit to placing it any later, and placing it earlier (before `ControlSystem.Update`) would risk scoring last month's control gold-transfer state rather than the current one. Spec 48's plan does not reorder any of the current `GameLogic.Update` system sequence, so this call slots in without disturbing anything else.
  - `LoadState(string saveName)`: after `RefreshSingletonEntities();`, add `OrgScoreSystem.Recompute(_world, _orgScoreCoefficient);` — a forced, ungated recompute so a freshly loaded save has correct scores immediately, not `0`/stale until the next boundary (same fix spec 47's plan applies for `CountryScoreSystem`, and the same dead-code trap `RebuildProximityMap()` fell into by never being called — this plan's `Recompute` call must actually be wired in, not left unused).

- **Wiring into `InitSystem`** (`src/Game.Main/InitSystem.cs`): add `OrgScoreSystem.Recompute(world, settings.OrgScoreCoefficient);` near the end of `Run`, **after** spec 48's multi-org creation loop (which replaces the current single-org block at lines 60-84, per spec 48's plan: `ResolveParticipatingOrgs` + a loop creating `Organization`/gold `Resource`/base `ControlEffect` per participating org) and **before** the final `world.Add(initEntity, new IsInitialized());`. `settings` is already loaded at line 41 (`var settings = context.GameSettings.Load();`), so `settings.OrgScoreCoefficient` is available with no extra config load. This placement mirrors `BuildProximityMap`'s existing "derived aggregate built once at init, right before `IsInitialized`" pattern and spec 47's plan's placement of `CountryScoreSystem.Recompute`.

- **`VisualState` exposure** (`src/Game.Main/VisualState.cs`): add `OrgScoreState : INotifyPropertyChanged` (idiom of `OrgMapState`/`ProvinceOwnershipState`/spec 47's `CountryScoreState`) exposing `IReadOnlyDictionary<string, double> ScoreByOrgId` and `Set(IReadOnlyDictionary<string, double> scoreByOrgId)` firing `PropertyChanged`. Add `public OrgScoreState OrgScore { get; } = new OrgScoreState();` to the `VisualState` aggregate class. **Design decision — expose all participating orgs, not just the view/player org.** `VisualStateConverter.Update(...)`'s existing signature takes a single `orgEntity` parameter (the player/view org) and every existing `Update*(world, orgEntity)` method (`UpdatePlayerOrganization`, `UpdateOrgMap`) is scoped to that one org's perspective (or, for `UpdateOrgMap`, aggregates *by country* across all orgs, not *by org*). Per this spec's acceptance criterion that score be queryable per-org across all participating orgs (needed for eventual cross-org comparison/ranking), `UpdateOrgScore` must **not** be keyed off the single `orgEntity` parameter — it iterates all `OrgScore` entities directly and builds the full `orgId -> score` map, the same unconditional-full-rebuild approach spec 47's plan uses for `UpdateCountryScore` (iterates all `CountryScore` entities, no per-view filtering).

- **`VisualStateConverter` population** (`src/Game.Main/VisualStateConverter.cs`): add `UpdateOrgScore(IReadOnlyWorld world)` — iterate `OrgScore` components, build the `orgId -> Value` dictionary, call `_state.OrgScore.Set(...)`. Call it from `Update(...)`'s existing sequence, alongside `UpdateOrgMap(world, orgEntity)` (no `orgEntity` argument needed for `UpdateOrgScore` itself, per the decision above). **Deliberately no dirty-check/version-counter machinery** (unlike `UpdateProvinceOwnership`'s `_lastSeenProvinceOwnershipVersion` guard) — `OrgScore` has as many entries as there are participating orgs (currently 2 per `organizations.json`: `Illuminati`, `Masons`; spec 48 defaults participation to all config entries), so rebuilding unconditionally every tick is negligible cost, matching `UpdateOrgMap`'s existing no-dirty-check style and spec 47's identical reasoning for `UpdateCountryScore`.

- **Spec 48 `SimulationResult`/`HeadlessRunner` JSON integration.** Per spec 48's plan, `src/Game.ConsoleRunner/SimulationResult.cs` defines a JSON shape where both the end-of-run `orgs` array and every `timeline[].orgs[]` entry contain one object per participating org with `orgId`, `totalControl`, `gold`. This plan adds a `"score": <double>` field to that same per-org object shape, populated identically in both places. **Confirmed user decision:** the score is emitted in **both** the end-of-run `orgs` array **and** every `timeline[].orgs[]` sample — this resolves spec 49's own `[NEEDS CLARIFICATION]` ambiguity in favor of the fuller, timeline-inclusive option (higher value for the eval/statistics work spec 48 named as its own future consumer, at negligible extra diff cost — one additional field write per existing per-sample per-org loop iteration). Concretely, wherever `HeadlessRunner.cs`'s per-sample metric-gathering loop currently calls `OrgMetrics.GetTotalControl(world, orgId)` and `OrgMetrics.GetGold(world, orgId)` to populate each sample's org entries (both the t0/final `orgs` array and each `timeline[]` entry), it must also call `OrgScoreSystem.GetScore(world, orgId)` and assign the result to the new `score` field — no separate recompute call is needed inside `HeadlessRunner` itself, since `OrgScoreSystem.Update` is already wired into the same `GameLogic.Update()` tick loop the headless runner drives (per this plan's `GameLogic` wiring above), and the t0/initial sample is already correct because `InitSystem.Run`'s forced `Recompute` (also wired above) runs before the runner takes its first sample. **Uncertainty flag:** the exact file/method name and loop structure inside `HeadlessRunner.cs` cannot be verified against real code today since spec 48 is unmerged and its plan.md is not locally readable — the implementing agent's first step (below) must re-confirm the actual per-sample metric-gathering code path in the merged `HeadlessRunner.cs` before wiring in the `OrgScoreSystem.GetScore` calls, rather than assuming this description's exact shape is literal.

- **No new `.asmdef`, no VContainer change, no UI change.** `Assets/Plugins/Core/` picks up the new types automatically on the next `dotnet build src/GlobalStrategy.Core.sln -c Release`.

## Steps

### Agent Steps

- [ ] **Confirm spec 48 has landed** — Before starting, verify `OrgMetrics.GetTotalControl`/`GetGold` exist under `src/Game.Systems/OrgMetrics.cs`, that `GameLogicContext.ParticipatingOrganizationIds`/`RngSeed` exist, that `InitSystem.Run` creates one `Organization`/gold `Resource`/base `ControlEffect` entity per participating org (not just `context.InitialOrganizationId`), and that `src/Game.ConsoleRunner/SimulationResult.cs`/`HeadlessRunner.cs` exist with the `orgs`/`timeline[].orgs[]` JSON shape described in this plan's Approach section. If any of this is not yet merged, stop — this plan's aggregation and JSON-integration code cannot compile or be tested without it. Re-read the actual merged `HeadlessRunner.cs` per-sample metric loop before wiring the `score` field, since this plan's description of it is inferred, not verified against real code.

- [ ] **Add the `OrgScore` component** — Create `src/Game.Components/OrgScore.cs`:
  ```csharp
  namespace GS.Game.Components {
  	// Not [Savable] — fully derivable from ControlEffect (via OrgMetrics.GetTotalControl)
  	// + the scoring coefficient; recomputed at init, at load, and at each month boundary
  	// by OrgScoreSystem. See ecs_patterns.md's derived-component convention.
  	public struct OrgScore {
  		public string OrgId;
  		public double Value;
  	}
  }
  ```

- [ ] **Add the `OrgScoreSystem`** — Create `src/Game.Systems/OrgScoreSystem.cs` with `Update(World, DateTime previousTime, DateTime currentTime, double coefficient)` (month-boundary gate, delegates to `Recompute`), `Recompute(World, double coefficient)` (forced aggregation per the Approach section above — iterates all `Organization` entities, calls `OrgMetrics.GetTotalControl` per org, destroys and recreates `OrgScore` entities), and `GetScore(IReadOnlyWorld, string orgId)` (linear scan, `0` if absent).

- [ ] **Add the config coefficient** — In `src/Game.Configs/GameSettings.cs`, add `public double OrgScoreCoefficient { get; set; } = 1.0;`. In `Assets/Configs/game_settings.json`, add `"orgScoreCoefficient": 1.0` alongside the existing keys (camelCase per `plugins.md`'s JSON convention), distinct from `countryScoreCoefficient`.

- [ ] **Seed initial score at init** — In `src/Game.Main/InitSystem.cs`'s `Run`, add `OrgScoreSystem.Recompute(world, settings.OrgScoreCoefficient);` after spec 48's multi-org creation loop and before the final `world.Add(initEntity, new IsInitialized());`, so scores are correct (including `0` for orgs with no control yet) from tick one.

- [ ] **Wire the monthly recompute into `GameLogic.Update`** — In `src/Game.Main/GameLogic.cs`, add `readonly double _orgScoreCoefficient;` set from `settings.OrgScoreCoefficient` in the constructor (alongside `_populationGrowthPercent`). In `Update(float deltaTime)`, add `OrgScoreSystem.Update(_world, _previousTime, currentTime, _orgScoreCoefficient);` immediately after the `ControlSystem.Update(_world, _previousTime, currentTime);` line (before `OpinionSystem.Update`), per the placement decision in the Approach section.

- [ ] **Force a recompute on load** — In `src/Game.Main/GameLogic.cs`'s `LoadState(string saveName)`, add `OrgScoreSystem.Recompute(_world, _orgScoreCoefficient);` directly after the existing `RefreshSingletonEntities();` call, so a freshly loaded save has correct, non-stale scores immediately.

- [ ] **Expose score via `VisualState`** — In `src/Game.Main/VisualState.cs`, add `OrgScoreState : INotifyPropertyChanged` with `IReadOnlyDictionary<string, double> ScoreByOrgId` and `Set(...)`, and add `public OrgScoreState OrgScore { get; } = new OrgScoreState();` to the `VisualState` class.

- [ ] **Populate score state every tick** — In `src/Game.Main/VisualStateConverter.cs`, add `UpdateOrgScore(IReadOnlyWorld world)` (iterate `OrgScore` components into a `Dictionary<string, double>`, call `_state.OrgScore.Set(...)`), and call it from `Update(...)`'s existing sequence next to `UpdateOrgMap(world, orgEntity)`. No version-counter/dirty-check — rebuild unconditionally every call, per the Approach section's reasoning.

- [ ] **Add the `score` field to spec 48's headless-runner JSON output** — In `src/Game.ConsoleRunner/SimulationResult.cs` (or wherever spec 48 defines the per-org result shape), add a `Score` (serialized `score`) `double` field to the per-org metric type used by both the `orgs` array and `timeline[].orgs[]`. In `HeadlessRunner.cs`'s per-sample metric-gathering code, call `OrgScoreSystem.GetScore(world, orgId)` alongside the existing `OrgMetrics.GetTotalControl`/`GetGold` calls and populate the new field, for every sample (t0, each month-boundary timeline entry, and the final end-of-run entry).

- [ ] **Add/extend tests** — Implement the Tests section below.

- [ ] **Rebuild the Core DLLs** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up `OrgScore`, `OrgScoreSystem`, the updated `GameSettings`, and the `GameLogic`/`InitSystem`/`VisualState`/`VisualStateConverter`/`SimulationResult`/`HeadlessRunner` changes.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script, so the only expected effect is the updated `Assets/Plugins/Core/*.dll` files and the new `orgScoreCoefficient` key in `Assets/Configs/game_settings.json` being picked up cleanly.

### 2. Sanity-check initial scores in Play mode

Enter Play mode. Using Unity MCP (or a temporary debug read), confirm every participating org's `OrgScoreSystem.GetScore` (or the equivalent value visible via `VisualState.OrgScore.ScoreByOrgId` if inspected through a debugger/log) is present and correct (including `0` for an org with no control yet) immediately at tick one — proving the `InitSystem.Run` forced recompute worked rather than waiting for the first month boundary.

### 3. Verify month-boundary recompute timing

Advance in-game time (e.g. via a debug time-multiplier or fast-forward) across a month boundary and confirm scores update only at the boundary, not continuously within a month, by checking values immediately before and after the boundary tick.

### 4. Verify control-change deferral and multi-org independence

Trigger a control change for one org mid-month (e.g. via `DebugChangeGoldCommand`'s sibling control-change path, or whatever mechanism grants `ControlEffect` at the time this is implemented) and confirm neither org's score changes until the next month-boundary tick, and that only the org whose control changed has a different score after that boundary — the other participating org's score is unaffected.

### 5. Verify save/load recompute

Save the game, reload it, and confirm scores are immediately correct (matching the control state at the moment of save) rather than reading `0` or a stale pre-save value until the next month boundary.

### 6. Verify the headless-runner JSON output

Run a headless simulation (per spec 48's CLI/execution model) and confirm the resulting JSON's `orgs` array and every `timeline[].orgs[]` entry contain a `score` field per org, with values consistent with `coefficient * totalControl` at each sample point.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case `[Fact]` names; harness pattern in `InitSystemTests.cs`/`ControlSystemTests.cs` — month-boundary date constants, `GameLogicContext`/`StaticConfig<T>` building, `MemoryStorage`, `CapturingSerializer`, `BuildLogic`).

- **New `src/Game.Tests/OrgScoreSystemTests.cs`:**
  - `score_computed_from_total_control_at_month_boundary` — an org with `ControlEffect` entries of value 20 and 30 across two countries, coefficient `0.5`, `OrgScoreSystem.Update(world, Jan31, Feb1, 0.5)` → `GetScore(world, orgId) == 25.0`.
  - `org_with_zero_control_has_zero_score` — an org with no `ControlEffect` entities → score `0` after recompute, not missing/error.
  - `score_unchanged_within_same_month` — `Update(world, Jan1, Jan2, coefficient)` (no month boundary crossed) leaves any previously-recomputed `OrgScore` untouched.
  - `control_change_mid_month_does_not_affect_score_until_boundary` — recompute once, mutate a `ControlEffect.Value` directly (or add a new one), then call `Update` with same-month dates → score unchanged until a real month-boundary `Update` call.
  - `multiple_months_skipped_recomputes_once_from_current_state` — `Update(world, Jan15, Mar20, coefficient)` (skips the Feb boundary entirely) still recomputes exactly once, from current control at call time (mirrors `ControlSystem`/spec 47's existing multi-month-skip semantics).
  - `multi_org_independence_one_orgs_control_change_does_not_move_another_orgs_score` — two orgs with independent `ControlEffect` entries; change one org's control, recompute → only that org's score changes, the other org's score is exactly as before.
  - `recompute_is_forced_and_ungated` — calling `Recompute` directly (not through `Update`) applies immediately regardless of any month-boundary condition — this is the entry point `InitSystem`/`LoadState` rely on.
  - `get_score_returns_zero_for_unknown_org` — `GetScore` on an `orgId` with no `OrgScore` entity returns `0`, not an exception.

- **Extend `src/Game.Tests/InitSystemTests.cs`:** add `org_score_seeded_at_init_from_initial_control` — after the first `GameLogic.Update()` (using the existing `BuildLogic` harness, extended per spec 48's shape to configure multiple participating orgs), every participating org's `OrgScoreSystem.GetScore` reflects `coefficient * sum(seed base ControlEffect for that org)`, confirming scores are correct from tick one rather than waiting for a month boundary. **Uncertainty flag:** this test's exact harness setup depends on how spec 48 extends `BuildLogic`/`GameLogicContext` to support multiple participating orgs (e.g. a `participatingOrganizationIds` parameter) — verify the actual extended harness shape once spec 48 is merged rather than assuming this description is literal.

- **Extend `src/Game.Tests/InitSystemTests.cs`:** add `org_score_recomputed_immediately_after_load`, alongside the existing `init_skipped_after_load`-style test (which already builds a `GameLogic` via the `GameLogicContext`/`MemoryStorage`/`CapturingSerializer`/`BuildLogic` harness and calls `logic.LoadState(...)`) — advance state so control differs from initial seed values, save, call `LoadState` on a fresh `GameLogic` instance built from the same harness, and assert `OrgScoreSystem.GetScore(...)` for at least one org immediately reflects the loaded control state — not `0`, not stale — proving the forced `Recompute` call added to `LoadState` works without relying on a subsequent month boundary.

- **Headless runner / `SimulationResult` JSON coverage:** whatever test file spec 48's own plan commits to for `HeadlessRunner`/`SimulationResult` (plausibly `src/Game.Tests/HeadlessRunnerTests.cs` or `SimulationResultTests.cs` — **name not verifiable today, spec 48 is unmerged and its plan.md is not locally readable**) needs extending with a test asserting the new `score` field appears, with the correct `coefficient * totalControl` value, in both the end-of-run `orgs` array and at least one `timeline[].orgs[]` entry after a short headless run. If no such file exists by the time this plan is implemented, add a new `src/Game.Tests/OrgScoreHeadlessOutputTests.cs` covering the same assertion instead of skipping this coverage.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* `OrgScore` (component), `OrgScoreSystem` (aggregation/recompute), and the `GameSettings`/`GameLogicContext`-adjacent wiring all live in `src/Game.Components`, `src/Game.Systems`, `src/Game.Configs`, `src/Game.Main`, `src/Game.ConsoleRunner` — no MonoBehaviour, no Unity-side logic. Nothing in this feature touches `Assets/Scripts/Unity/*`.
- *VContainer sole DI.* No new registrations needed — this feature adds no Unity-side consumer; `VisualState`/`GameLogic` are already resolved through the existing container wiring untouched by this plan.
- *UI Toolkit only.* No UI surface is added or modified — score consumption (any future HUD panel/leaderboard) is explicitly out of scope per the spec.
- *URP only.* No rendering, shader, or material change.
- *One `.asmdef` per feature folder.* Not applicable — this Constitution bullet is scoped to `Assets/Scripts/`; this feature only touches `src/` (`.csproj`-based, no `.asmdef` involved).
- *Planning/Specification discipline.* This plan follows an approved spec (`Docs/Specs/49_org-scoring/spec.md`) per the standard `/specify` → `/plan` sequence, and explicitly gates implementation start on its stated dependency — spec 48 (PR #12, unmerged) — landing first, exactly as spec 47's plan gated on spec 46.
- *File organisation.* Plan lives at `Docs/Specs/49_org-scoring/plan.md`, matching its spec's directory — correct index, correct pairing.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching all surrounding files shown in this plan (`ControlSystem.cs`, `ProvinceOwnershipVersion.cs`, and spec 47's `CountryScore.cs`/`CountryScoreSystem.cs` precedent).

Use /implement to start working on the plan or request changes.
