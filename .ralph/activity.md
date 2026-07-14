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
