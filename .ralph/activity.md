# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-14 — Add OwnerType.Province enum case

Task: "Add OwnerType.Province enum case" (src).

Change: Added `Province` as a fourth case to `OwnerType` enum in
`src/Game.Components/OwnerType.cs`, after `Character`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add ProvinceEntry.Population field" in
`src/Game.Configs/ProvinceConfig.cs`. No blockers encountered.

---

## 2026-07-14 — Add ProvinceEntry.Population field

Task: "Add ProvinceEntry.Population field" (src).

Change: Added `public double Population { get; set; }` to `ProvinceEntry` in
`src/Game.Configs/ProvinceConfig.cs`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Pass population through Stage 2 ProvinceProcessor" in
`src/Game.Configs.Loader/ProvinceProcessor.cs` — add a `GetDoubleProp` helper mirroring
`GetStringProp` and set `Population` on the constructed `ProvinceEntry` from the new
`population` feature property (default 0.0 if absent). No blockers encountered.

---

## 2026-07-14 — Pass population through Stage 2 ProvinceProcessor

Task: "Pass population through Stage 2 ProvinceProcessor" (src).

Change: In `src/Game.Configs.Loader/ProvinceProcessor.cs`, added a `GetDoubleProp` helper
mirroring `GetStringProp` (returns `0.0` if the property is absent). `Process` now reads the
`population` property per feature via `GetDoubleProp(props, "population")` and sets it on the
constructed `ProvinceEntry.Population`. `countryId` cross-validation logic (`FindByCountryId`
check + `validationErrors`) is untouched.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add GameSettings.PopulationGrowthPercentPerMonth" —
add `public double PopulationGrowthPercentPerMonth { get; set; } = 0.075;` to
`src/Game.Configs/GameSettings.cs` alongside `StartYear`/`SpeedMultipliers`/`DefaultLocale`/
`AutoSaveInterval`, and add `"populationGrowthPercentPerMonth": 0.075` to
`Assets/Configs/game_settings.json`. No blockers encountered.

---

## 2026-07-14 — Add GameSettings.PopulationGrowthPercentPerMonth

Task: "Add GameSettings.PopulationGrowthPercentPerMonth" (config).

Change: Added `public double PopulationGrowthPercentPerMonth { get; set; } = 0.075;` to
`GameSettings` in `src/Game.Configs/GameSettings.cs`, alongside the existing
`StartYear`/`SpeedMultipliers`/`DefaultLocale`/`AutoSaveInterval` properties. Added
`"populationGrowthPercentPerMonth": 0.075` to `Assets/Configs/game_settings.json`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add ProvincePopulationGrowthSystem" — create
`src/Game.Systems/ProvincePopulationGrowthSystem.cs` with `PopulationResourceId = "population"`
and a static `Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent)`
that mirrors `ResourceSystem`/`ControlSystem`'s month-boundary detection, then iterates matching
archetypes for `OwnerType.Province` + `ResourceId == PopulationResourceId` rows and multiplies
`Value` in place (direct array-index mutation, no lambda — see `.claude/rules/unity/ecs_patterns.md`
on `ref`/lambda restrictions). No blockers encountered.

---

## 2026-07-14 — Add ProvincePopulationGrowthSystem

Task: "Add ProvincePopulationGrowthSystem" (src).

Change: Created `src/Game.Systems/ProvincePopulationGrowthSystem.cs` with
`public const string PopulationResourceId = "population"` and a static
`Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent)`
that computes `isMonthBoundary` the same way as `ResourceSystem`/`ControlSystem` (month or year
differs) and returns early if not crossed. Iterates
`world.GetMatchingArchetypes({TypeId<ResourceOwner>.Value, TypeId<Resource>.Value}, null)` and,
for rows where `owners[i].OwnerType == OwnerType.Province && resources[i].ResourceId ==
PopulationResourceId`, multiplies `resources[i].Value` in place by
`1.0 + monthlyGrowthPercent / 100.0` via direct array-index mutation (no lambda, matching the
`ref`/lambda restriction in `.claude/rules/unity/ecs_patterns.md`). `ResourceEffect`/
`ResourceLink`/`PayType` are untouched — this system reads only `ResourceOwner`+`Resource`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Seed province population entities at init" — in
`src/Game.Main/InitSystem.Run`, capture `var provinceConfig = context.Province.Load();` before
`ProvinceOwnershipSystem.Seed(world, provinceConfig);`, then add a
`CreateProvincePopulationEntities(world, provinceConfig);` call right after that creates one
entity per `ProvinceEntry` with `ResourceOwner(entry.ProvinceId, OwnerType.Province)` and
`Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value =
entry.Population }`. No blockers encountered.

---

## 2026-07-14 — Seed province population entities at init

Task: "Seed province population entities at init" (src).

Change: In `src/Game.Main/InitSystem.cs`, `Run` now captures `var provinceConfig =
context.Province.Load();` and passes it to both `ProvinceOwnershipSystem.Seed(world,
provinceConfig)` and a new `CreateProvincePopulationEntities(world, provinceConfig)` call
(placed right after `Seed`). Added `static void CreateProvincePopulationEntities(World world,
ProvinceConfig config)`: for each `ProvinceEntry` in `config.Provinces`, creates one entity with
`ResourceOwner(entry.ProvinceId, OwnerType.Province)` and `Resource { ResourceId =
ProvincePopulationGrowthSystem.PopulationResourceId, Value = entry.Population }`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Wire population growth into GameLogic.Update" — in
`src/Game.Main/GameLogic.cs`, add a `readonly double _populationGrowthPercent;` field set from
`settings.PopulationGrowthPercentPerMonth` in the constructor (same place `_speedMultipliers` is
captured), and in `Update`, immediately after `OpinionSystem.Update(_world, _previousTime,
currentTime);`, call `ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime,
_populationGrowthPercent);`. No blockers encountered.


---

## 2026-07-15 -- Wire population growth into GameLogic.Update

Task: "Wire population growth into GameLogic.Update" (src).

Change: The required wiring was already present in `src/Game.Main/GameLogic.cs` from the prior
iteration's work (field `readonly double _populationGrowthPercent;` declared, assigned from
`settings.PopulationGrowthPercentPerMonth` in the constructor alongside `_speedMultipliers`, and
`ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime,
_populationGrowthPercent);` called in `Update` immediately after `OpinionSystem.Update(...)`).
No code changes were needed this iteration -- verified the existing state matches the task spec
exactly and ran the gate to confirm.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` -> Build succeeded, 0 Warning(s), 0 Error(s).

Notes for next iteration: Next task is "Add ProvincePopulationGrowthSystemTests" -- create
`src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` mirroring `ResourceSystemTests.cs`'s
Jan31/Feb1/Jan1/Jan2 constants and world-building helpers, covering: no growth within the same
month, growth by percent at a month boundary (1000 -> 1000.75 at 0.075%), compounding across
multiple month boundaries, only `OwnerType.Province` + `population` resource id rows affected
(country-owned population and province-owned non-population resources untouched), and two
province population resources diverging independently. Gate is `dotnet test
src/GlobalStrategy.Core.sln`. No blockers encountered; this is the first task in the loop whose
gate is `dotnet test` rather than a build-only gate.