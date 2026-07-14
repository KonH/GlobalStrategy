# Spec: Country Scoring

## Feature Intent

As a game designer, I want each country to have a numeric score derived from the total population of the provinces it currently owns multiplied by a config-defined coefficient, recalculated once per in-game month after population updates, so that country strength/progress becomes a queryable, comparable value that other systems (or a future UI/leaderboard/AI layer) can consume without each of them re-deriving population aggregation themselves.

## Dependency

This feature depends on `Docs/Specs/46_province-population/spec.md` (branch `feature/province-population-spec`, not yet merged), which establishes per-province population as a `Resource{ResourceId="population", Value}` entity owned via `ResourceOwner(provinceId, OwnerType.Province)`, seeded at init and grown monthly by a standalone `ProvincePopulationGrowthSystem`. This spec's population aggregation reads that same `Resource`/`ResourceOwner` shape — filtering to `OwnerType.Province` and `ResourceId == "population"` — rather than inventing a second population representation. Implementation of this feature cannot start before that dependency lands.

## Acceptance Criteria

- **Given** a country currently owns one or more provinces, each carrying a `Resource{ResourceId="population"}` entity (per the province-population dependency above), and a configured scoring coefficient **When** the monthly score recalculation runs **Then** the country's score is set to `coefficient * sum(population of all provinces currently owned by that country)`.
- **Given** a country currently owns zero provinces (e.g. fully conquered/eliminated) **When** the monthly score recalculation runs **Then** that country's score is `0`, not an error and not a skipped/missing value.
- **Given** the runtime province ownership model (`VisualState.ProvinceOwnership.OwnerByProvinceId`, seeded from `province_config.json` at startup per `.claude/rules/unity/map_system.md`) **When** score is computed for a country **Then** "owned provinces" means provinces whose *current runtime owner* is that country, not the static seed `countryId` recorded in `province_config.json` — so a province that has changed hands (e.g. via `DebugChangeProvinceOwnerCommand`) contributes to its new owner's score, not its original one.
- **Given** the existing recalculation precedent in `ControlSystem`/`ResourceSystem` (both compare `previousTime` vs `currentTime` month/year and gate their monthly work behind `isMonthBoundary`) **When** this feature adds its own recalculation step (e.g. a `CountryScoreSystem` invoked from `GameLogic.Update()` alongside those systems) **Then** it follows the same `isMonthBoundary` gating pattern rather than introducing a separate/new timing mechanism.
- **Given** a province's owner changes mid-month (e.g. via the debug ownership-reassignment cheat) **When** the change is applied **Then** no country's score changes immediately as a side effect of that reassignment — scores remain at their last-computed value until the next month-boundary recalculation runs, consistent with how `ControlSystem`/`ResourceSystem` defer their own monthly effects.
- **Given** the game advances multiple in-game months within a single `Update()` call (e.g. under a fast time multiplier that skips over more than one month boundary between frames) **When** the monthly recalculation runs **Then** score is recomputed exactly once using each country's current total owned population at that point — mirroring `ControlSystem`/`ResourceSystem`'s existing semantics of comparing only `previousTime` vs `currentTime` month/year, not iterating or accumulating per skipped month.
- **Given** population values change during a month (e.g. via whatever mechanism updates per-province population, out of scope for this feature to define) **When** the game is not at a month boundary **Then** country score is not recalculated from the changed population until the next month boundary is reached.
- **Given** a country's score has been computed **When** any other system or a future UI needs to read it **Then** it is exposed per-country in a queryable form (e.g. alongside other per-country runtime state such as `VisualState.ProvinceOwnership`) without requiring the consumer to re-aggregate population itself.
- **Given** the scoring coefficient is a single global tunable value (not per-country) **When** it needs to change for game balancing **Then** it is adjustable via a config value (e.g. alongside `Assets/Configs/game_settings.json`'s existing tunables such as `startYear`/`autoSaveInterval`) without requiring a code change.

## Out of Scope

- Defining or implementing per-province population as a data source, its seeding, or its monthly growth mechanic — that is spec 46 (`province-population`), a dependency of this feature, not part of it. This feature only reads the `Resource{ResourceId="population"}` values that spec produces.
- Any UI, HUD panel, leaderboard, ranking screen, or other player-facing display of country scores.
- Any consumption of score by AI decision-making, win/loss conditions, or any other gameplay system beyond making the value available.
- Any change to `ControlSystem`, `ResourceSystem`, or the resource/control-effect mechanics themselves — this feature only follows their existing month-boundary timing pattern, it does not modify them.
- Any change to how province ownership itself is assigned or changed (debug cheat, future conquest mechanics) — this feature only reads current ownership, per `.claude/rules/unity/map_system.md`.
- Historical score tracking (e.g. score-over-time graphs, previous months' scores) — only the current score value is in scope.
- Per-province or per-owned-territory score breakdowns — only a single aggregate score per country is in scope.

## Ambiguities

- [NEEDS CLARIFICATION: What consumes the country score once computed? The user's request only specifies the computation, not a consumer (UI display, win condition, leaderboard, AI input). This spec writes acceptance criteria assuming the score only needs to be computed and exposed in queryable form — confirm that's sufficient for this pass, or name the actual consumer so its own requirements can be captured.]
- [NEEDS CLARIFICATION: On loading a save, should score be recomputed immediately at load time (so a freshly loaded game shows an up-to-date score), or left at its last-persisted value until the next month boundary (consistent with `ControlSystem`/`ResourceSystem`'s existing "only recompute at month boundary" behavior, but potentially showing a stale score right after load)? Related: should `Score` be `[Savable]` at all, or derived fresh at startup like `ProximityMapData` — per `.claude/rules/unity/ecs_patterns.md`, this depends on whether "recomputed monthly, not at startup" makes it a non-derivable, must-persist value, or whether an at-load recompute makes it safe to omit `[Savable]`.]
