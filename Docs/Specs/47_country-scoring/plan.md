# Plan: Country Scoring

## Spec

Source: `Docs/Specs/47_country-scoring/spec.md`.

**Intent.** Give each country a numeric score — `coefficient * sum(population of provinces it currently owns)` — recomputed once per in-game month (after population updates), plus forced recomputes at init and on load, so the value is always current and queryable by any future consumer (UI, leaderboard, AI) without that consumer re-deriving population aggregation itself. Pure `src/` ECS feature; no UI, no MonoBehaviour, no new Unity assembly.

**Dependency.** Depends on `Docs/Specs/46_province-population/spec.md` (branch `feature/province-population-spec`, not yet merged at plan time), which is expected to add an `OwnerType.Province` enum value and seed `Resource{ResourceId="population"}` entities owned via `ResourceOwner(provinceId, OwnerType.Province)`. This plan's aggregation step reads that exact shape and must not invent a second population representation. **Implementation of this plan cannot start until that dependency has landed** — this document is written assuming its shape as described in the spec, and the aggregation code in `CountryScoreSystem.Recompute` will not compile against `OwnerType.Province` until the dependency merges.

**Key acceptance criteria (design targets):**
- Score = `coefficient * sum(population of provinces currently owned by that country)`, recomputed at each month boundary (`isMonthBoundary`, same gating pattern as `ResourceSystem`/`ControlSystem`).
- A country owning zero provinces has score `0`, never an error or a missing value.
- "Owned provinces" means current *runtime* owner (`VisualState.ProvinceOwnership.OwnerByProvinceId` / `ProvinceOwnershipSystem.GetProvincesByOwner`), not the static seed `countryId` from `province_config.json`.
- A province changing owner mid-month does not change any score until the next month-boundary recompute — no side-effect recompute on `ChangeOwner`.
- Skipping multiple month boundaries in one `Update()` call still recomputes exactly once, from current state at call time (mirrors `ResourceSystem`/`ControlSystem` semantics — no per-skipped-month iteration).
- Population changes mid-month do not move the score until the next boundary.
- Score is exposed per-country in `VisualState` (queryable), with no consumer implemented in this feature.
- The scoring coefficient is a single global config value (`game_settings.json`), not per-country.
- Score is not persisted (`[Savable]` omitted, per `ecs_patterns.md`'s derived-component convention) and is recomputed immediately at init and at load — not left at `0`/stale until the next boundary.

**Out of scope:** province population itself (spec 46 owns that), any score consumer (UI/win-conditions/AI), changes to `ControlSystem`/`ResourceSystem` internals, changes to how ownership is assigned, historical score tracking, per-province score breakdowns.

## Goal

Add a `CountryScore` runtime component and a `CountryScoreSystem` that aggregates each country's currently-owned provinces' population (via the existing `ProvinceOwnershipSystem.GetProvincesByOwner` primitive) into a single `coefficient * totalPopulation` value per country, recomputed on the same month-boundary cadence already used by `ResourceSystem`/`ControlSystem`, plus forced ungated recomputes at init (`InitSystem.Run`) and on load (`GameLogic.LoadState`) so the value is never stale or zero when it shouldn't be. Expose the result through a new `VisualState.CountryScore` slice, rebuilt unconditionally every tick by `VisualStateConverter` (bounded ~163-country dictionary, no dirty-check machinery needed). No consumer, no UI, no persistence — this plan only computes and exposes the value.

## Approach

- **New component** `src/Game.Components/CountryScore.cs` — `public struct CountryScore { public string CountryId; public double Value; }`, explicitly **not** `[Savable]`, with a comment following the `ProvinceOwnershipVersion.cs` convention: fully derivable from province population + current ownership + the coefficient, so persisting it is wasted space/risk with no benefit.
- **New system** `src/Game.Systems/CountryScoreSystem.cs`, static, mirroring `ResourceSystem`/`ControlSystem`'s archetype-iteration style:
  - `Update(World world, DateTime previousTime, DateTime currentTime, double coefficient)` — computes `isMonthBoundary` exactly like `ResourceSystem.Update` (`previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year`), early-returns if not crossed, else calls `Recompute`.
  - `Recompute(World world, double coefficient)` — the forced, ungated entry point. Unlike `ResourceSystem`/`ControlSystem`, this system needs one because `InitSystem.Run` and `GameLogic.LoadState` both need a correct value with no dependency on the next month boundary (this is exactly the gap that left `ProximityMapData` stale/dead via the never-called `GameLogic.RebuildProximityMap()` — this plan does not repeat that mistake, see Steps below).
  - `GetScore(IReadOnlyWorld world, string countryId)` — linear scan returning the matching `Value` or `0` if no entity found (mirrors `ProvinceOwnershipSystem.GetOwner`'s shape/fallback).
  - Aggregation algorithm inside `Recompute`: (1) `ProvinceOwnershipSystem.GetProvincesByOwner(world)` → `Dictionary<string, List<string>>` of `ownerId -> provinceIds`; (2) one pass over archetypes matching `ResourceOwner` + `Resource`, building `Dictionary<string, double>` of `provinceId -> population`, filtering to `owners[i].OwnerType == OwnerType.Province && resources[i].ResourceId == "population"` (use `ProvincePopulationGrowthSystem.PopulationResourceId` if spec 46 defines that constant by implementation time, to avoid a duplicate `"population"` string literal — otherwise a local literal, matching the existing style of hardcoded resource ids like `"gold"` in `GameLogic.ApplyDebugChangeGold`); (3) destroy all existing `CountryScore` entities (destroy-then-recreate, same style as `InitSystem.BuildProximityMap`'s handling of `ProximityMapData`); (4) iterate all `Country` entities, for each `countryId` sum the population of its provinces from the ownership dictionary (0 if the country has no entry — satisfies the "zero provinces → score 0" criterion), multiply by `coefficient`, create a fresh `CountryScore { CountryId, Value }` entity.
- **Config**: add `public double CountryScoreCoefficient { get; set; } = 1.0;` to `src/Game.Configs/GameSettings.cs` and `"countryScoreCoefficient": 1.0` to `Assets/Configs/game_settings.json`. **`1.0` is a placeholder pending game-design balancing** — no tuning guidance was given for this feature (unlike spec 46's researched `0.075`/month population-growth figure); this plan does not attempt to derive a "correct" value.
- **Wiring into `GameLogic`** (`src/Game.Main/GameLogic.cs`):
  - Constructor: add `readonly double _countryScoreCoefficient;` field, set from `settings.CountryScoreCoefficient` alongside the existing `_speedMultipliers = settings.SpeedMultipliers;` line.
  - `Update(float deltaTime)`: add `CountryScoreSystem.Update(_world, _previousTime, currentTime, _countryScoreCoefficient);` immediately after `OpinionSystem.Update(_world, _previousTime, currentTime);` (current code — no `ProvincePopulationGrowthSystem` call exists yet since spec 46 hasn't merged). **Once spec 46 lands**, this call must move to immediately after its `ProvincePopulationGrowthSystem.Update(...)` call instead — population-growth-then-score must hold within the same tick so score reflects the just-grown population for that month, not last month's. This plan's own step list places the call after `OpinionSystem.Update` as the immediately-correct location today, and flags the reordering as a required follow-up once spec 46 merges (call out explicitly in code review / picked up naturally if `CountryScoreSystem.Update` is implemented after spec 46 merges, in which case place it after `ProvincePopulationGrowthSystem.Update` directly).
  - `LoadState(string saveName)`: after `RefreshSingletonEntities();`, add `CountryScoreSystem.Recompute(_world, _countryScoreCoefficient);` — a forced, ungated recompute so a freshly loaded save has correct scores immediately, not `0`/stale until the next boundary. This directly avoids repeating the `RebuildProximityMap()` dead-code trap (that method exists but is never called anywhere in the codebase, so `ProximityMapData` silently stays wiped after every load — confirmed via a full-repo grep). `CountryScoreSystem.Recompute` must actually be invoked here, not left as an unused public method.
- **Wiring into `InitSystem`** (`src/Game.Main/InitSystem.cs`): add `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` near the end of `Run` (after `ProvinceOwnershipSystem.Seed(world, context.Province.Load());` at line 37, and after spec 46's population-seeding call — which per the spec 46 dependency note is inserted directly after that same `Seed` call — and after `var settings = context.GameSettings.Load();` at line 39, so `settings.CountryScoreCoefficient` is available with no extra config load). Placing it right before the final `world.Add(initEntity, new IsInitialized());` keeps it consistent with `BuildProximityMap`'s existing "derived aggregate built once at init" placement.
- **`VisualState` exposure** (`src/Game.Main/VisualState.cs`): add `CountryScoreState : INotifyPropertyChanged` (idiom of `OrgMapState`/`ProvinceOwnershipState`) exposing `IReadOnlyDictionary<string, double> ScoreByCountryId` and `Set(IReadOnlyDictionary<string, double> scoreByCountryId)` firing `PropertyChanged`. Add `public CountryScoreState CountryScore { get; } = new CountryScoreState();` to the `VisualState` aggregate class.
- **`VisualStateConverter` population** (`src/Game.Main/VisualStateConverter.cs`): add `UpdateCountryScore(world)` — iterate `CountryScore` components, build the `countryId -> Value` dictionary, call `_state.CountryScore.Set(...)`. Call it from `Update(...)`'s existing sequence, alongside `UpdateProvinceOwnership(world)`. **Deliberately no dirty-check/version-counter machinery** (unlike `UpdateProvinceOwnership`'s `_lastSeenProvinceOwnershipVersion` guard) — `CountryScore` only has ~163 entries (bounded by available countries), so it is cheap to rebuild unconditionally every tick, the same way `UpdateOrgMap` already rebuilds its entire by-country dictionary with no dirty-check at all. This is a deliberate simplicity choice, not an oversight — `ProvinceOwnershipSystem`'s version counter exists because province ownership is queried broadly (per-province, ~1200+ entries) and changes are rare; country score is small and already recomputed at a bounded cadence (month boundary / init / load) by `CountryScoreSystem` itself, so a second layer of dirty-checking on top would be redundant.
- **No new `.asmdef`, no VContainer change, no UI change.** `Assets/Plugins/Core/` picks up the new types automatically on the next `dotnet build src/GlobalStrategy.Core.sln -c Release`.

## Steps

### Agent Steps

- [ ] **Confirm spec 46 has landed** — Before starting, verify `OwnerType.Province` exists in `src/Game.Components/OwnerType.cs` and that province-population `Resource{ResourceId="population"}` entities (owned via `ResourceOwner(provinceId, OwnerType.Province)`) are seeded by `InitSystem.Run` and grown monthly. If not yet merged, stop — this plan's aggregation code cannot compile or be tested without it.

- [ ] **Add the `CountryScore` component** — Create `src/Game.Components/CountryScore.cs`:
  ```csharp
  namespace GS.Game.Components {
  	// Not [Savable] — fully derivable from province population + current ownership +
  	// the scoring coefficient; recomputed at init, at load, and at each month boundary
  	// by CountryScoreSystem. See ecs_patterns.md's derived-component convention.
  	public struct CountryScore {
  		public string CountryId;
  		public double Value;
  	}
  }
  ```

- [ ] **Add the `CountryScoreSystem`** — Create `src/Game.Systems/CountryScoreSystem.cs` with `Update(World, DateTime previousTime, DateTime currentTime, double coefficient)` (month-boundary gate, delegates to `Recompute`), `Recompute(World, double coefficient)` (forced aggregation per the Approach section above — reads `ProvinceOwnershipSystem.GetProvincesByOwner`, sums population per owner, destroys and recreates `CountryScore` entities for every `Country`), and `GetScore(IReadOnlyWorld, string countryId)` (linear scan, `0` if absent).

- [ ] **Add the config coefficient** — In `src/Game.Configs/GameSettings.cs`, add `public double CountryScoreCoefficient { get; set; } = 1.0;`. In `Assets/Configs/game_settings.json`, add `"countryScoreCoefficient": 1.0` alongside the existing `startYear`/`speedMultipliers`/`defaultLocale`/`autoSaveInterval` keys (camelCase per `plugins.md`'s JSON convention).

- [ ] **Seed initial score at init** — In `src/Game.Main/InitSystem.cs`'s `Run`, add `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` near the end of `Run` (after province population has been seeded and after `var settings = context.GameSettings.Load();`, before the final `world.Add(initEntity, new IsInitialized());`), so scores are non-zero from tick one.

- [ ] **Wire the monthly recompute into `GameLogic.Update`** — In `src/Game.Main/GameLogic.cs`, add `readonly double _countryScoreCoefficient;` set from `settings.CountryScoreCoefficient` in the constructor (alongside `_speedMultipliers`). In `Update(float deltaTime)`, add `CountryScoreSystem.Update(_world, _previousTime, currentTime, _countryScoreCoefficient);` immediately after the `OpinionSystem.Update(_world, _previousTime, currentTime);` line (current call order at time of writing — no `ProvincePopulationGrowthSystem` call exists in `GameLogic.cs` yet). If spec 46's `ProvincePopulationGrowthSystem.Update(...)` call has already been wired into `GameLogic.Update` by the time this step is implemented, place the `CountryScoreSystem.Update` call immediately after that call instead, so population growth is guaranteed to run first within the same tick.

- [ ] **Force a recompute on load** — In `src/Game.Main/GameLogic.cs`'s `LoadState(string saveName)`, add `CountryScoreSystem.Recompute(_world, _countryScoreCoefficient);` directly after the existing `RefreshSingletonEntities();` call, so a freshly loaded save has correct, non-stale scores immediately (the same fix `RebuildProximityMap()` should have received but never did, per the dead-code finding above — do not leave this plan's recompute as an unused method).

- [ ] **Expose score via `VisualState`** — In `src/Game.Main/VisualState.cs`, add `CountryScoreState : INotifyPropertyChanged` with `IReadOnlyDictionary<string, double> ScoreByCountryId` and `Set(...)`, and add `public CountryScoreState CountryScore { get; } = new CountryScoreState();` to the `VisualState` class.

- [ ] **Populate score state every tick** — In `src/Game.Main/VisualStateConverter.cs`, add `UpdateCountryScore(IReadOnlyWorld world)` (iterate `CountryScore` components into a `Dictionary<string, double>`, call `_state.CountryScore.Set(...)`), and call it from `Update(...)`'s existing sequence next to `UpdateProvinceOwnership(world)`. No version-counter/dirty-check — rebuild unconditionally every call, matching `UpdateOrgMap`'s existing style, per the Approach section's reasoning.

- [ ] **Add/extend tests** — Implement the Tests section below.

- [ ] **Rebuild the Core DLLs** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up `CountryScore`, `CountryScoreSystem`, the updated `GameSettings`, `GameLogicContext`-adjacent `GameLogic`/`InitSystem`/`VisualState`/`VisualStateConverter` changes.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script, so the only expected effect is the updated `Assets/Plugins/Core/*.dll` files and the new `countryScoreCoefficient` key in `Assets/Configs/game_settings.json` being picked up cleanly.

### 2. Sanity-check initial scores in Play mode

Enter Play mode. Using Unity MCP (or a temporary debug read), confirm every available country's `CountryScoreSystem.GetScore` (or the equivalent value visible via `VisualState.CountryScore.ScoreByCountryId` if inspected through a debugger/log) is non-zero immediately at tick one — proving the `InitSystem.Run` forced recompute worked rather than waiting for the first month boundary.

### 3. Verify month-boundary recompute timing

Advance in-game time (e.g. via a debug time-multiplier or fast-forward) across a month boundary and confirm scores update only at the boundary, not continuously within a month, by checking values immediately before and after the boundary tick.

### 4. Verify ownership-change deferral

Trigger `DebugChangeProvinceOwnerCommand` (existing debug cheat) mid-month and confirm neither the old nor the new owner's score changes until the next month-boundary tick.

### 5. Verify save/load recompute

Save the game, reload it, and confirm scores are immediately correct (matching the population/ownership state at the moment of save) rather than reading `0` or a stale pre-save value until the next month boundary.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case `[Fact]` names; harness pattern in `InitSystemTests.cs`/`ControlSystemTests.cs`/`ResourceSystemTests.cs` — month-boundary date constants, `GameLogicContext`/`StaticConfig<T>` building, `MemoryStorage`, `CapturingSerializer`, `BuildLogic`).

- **New `src/Game.Tests/CountryScoreSystemTests.cs`:**
  - `score_computed_from_owned_province_population_at_month_boundary` — a country owning two provinces with population `Resource`s of 1000 and 2000, coefficient `0.01`, `CountryScoreSystem.Update(world, Jan31, Feb1, 0.01)` → `GetScore(world, countryId) == 30.0`.
  - `country_with_zero_owned_provinces_has_zero_score` — a country with no entry in `ProvinceOwnershipSystem.GetProvincesByOwner` → score `0` after recompute, not missing/error.
  - `score_unchanged_within_same_month` — `Update(world, Jan1, Jan2, coefficient)` (no month boundary crossed) leaves any previously-recomputed `CountryScore` untouched.
  - `ownership_change_mid_month_does_not_affect_score_until_boundary` — recompute once, call `ProvinceOwnershipSystem.ChangeOwner`, then call `Update` with same-month dates → scores for both old and new owner unchanged until a real month-boundary `Update` call.
  - `multiple_months_skipped_recomputes_once_from_current_state` — `Update(world, Jan15, Mar20, coefficient)` (skips the Feb boundary entirely) still recomputes exactly once, from current population/ownership at call time (mirrors `ControlSystem`/`ResourceSystem`'s existing multi-month-skip semantics).
  - `recompute_reads_current_runtime_owner_not_seed_country_id` — seed a province owned by country A, call `ProvinceOwnershipSystem.ChangeOwner` to country B, then `Recompute` directly → the province's population counts toward B's score, not A's.
  - `recompute_is_forced_and_ungated` — calling `Recompute` directly (not through `Update`) applies immediately regardless of any month-boundary condition — this is the entry point `InitSystem`/`LoadState` rely on.
  - `get_score_returns_zero_for_unknown_country` — `GetScore` on a `countryId` with no `CountryScore` entity returns `0`, not an exception.

- **Extend `src/Game.Tests/InitSystemTests.cs`:** add `country_score_seeded_at_init_from_province_population` — after `InitSystem.Update`/the first `GameLogic.Update` (whichever the existing harness in that file uses), every available country's `CountryScoreSystem.GetScore` reflects `coefficient * sum(seed population of its owned provinces)`, confirming scores are non-zero from tick one rather than waiting for a month boundary.

- **Extend `src/Game.Tests/SaveLoadRoundTripTests.cs`:** add `country_score_recomputed_immediately_after_load` — build a `GameLogic`, advance state so population/ownership differ from initial seed values, save, then call `LoadState` on a fresh `GameLogic` instance (or reuse whatever round-trip pattern the file already uses) and assert `CountryScoreSystem.GetScore(...)` for at least one country immediately reflects the loaded population/ownership — not `0`, not stale — proving the forced `Recompute` call added to `LoadState` works without relying on a subsequent month boundary.

- **`src/Game.Tests/ProvinceOwnershipTests.cs`:** no changes needed — the runtime-owner-vs-seed-id behavior this feature depends on is already covered there, and `CountryScoreSystemTests.cs`'s `recompute_reads_current_runtime_owner_not_seed_country_id` test covers the score-specific angle without duplicating ownership-system coverage.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* `CountryScore` (component), `CountryScoreSystem` (aggregation/recompute), and the `GameSettings`/`GameLogicContext`-adjacent wiring all live in `src/Game.Components`, `src/Game.Systems`, `src/Game.Configs`, `src/Game.Main` — no MonoBehaviour, no Unity-side logic. Nothing in this feature touches `Assets/Scripts/Unity/*`.
- *VContainer sole DI.* No new registrations needed — this feature adds no Unity-side consumer; `VisualState`/`GameLogic` are already resolved through the existing container wiring untouched by this plan.
- *UI Toolkit only.* No UI surface is added or modified — score consumption (any future HUD panel) is explicitly out of scope per the spec.
- *URP only.* No rendering, shader, or material change.
- *One `.asmdef` per feature folder.* All new files land in existing feature folders (`src/Game.Components/`, `src/Game.Systems/`, `src/Game.Configs/`, plus edits inside existing `src/Game.Main/` files). No new folder or assembly is introduced.
- *Planning/Specification discipline.* This plan follows an approved spec (`Docs/Specs/47_country-scoring/spec.md`) per the standard `/specify` → `/plan` sequence, and explicitly gates implementation start on its stated dependency (spec 46) landing first.
- *File organisation.* Plan lives at `Docs/Specs/47_country-scoring/plan.md`, matching its spec's directory — correct index, correct pairing.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching all surrounding files shown in this plan (`ResourceSystem.cs`, `ProvinceOwnershipSystem.cs`, `ProvinceOwnershipVersion.cs`).

Use /implement to start working on the plan or request changes.
