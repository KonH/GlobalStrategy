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
---

## 2026-07-15 -- Add ProvincePopulationGrowthSystemTests

Task: "Add ProvincePopulationGrowthSystemTests" (test).

Change: Created `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` mirroring
`ResourceSystemTests.cs`'s Jan31/Feb1/Jan1/Jan2 constants and world-building helper pattern
(`CreateWorldWithResource` takes an explicit `OwnerType` since this system keys off
`OwnerType.Province` rather than a fixed owner type). Five facts: `population_unaffected_within_same_month`
(Jan1->Jan2, value unchanged), `population_grows_by_percent_at_month_boundary` (Jan31->Feb1,
1000 -> 1000.75 at 0.075%), `growth_compounds_across_multiple_months` (two successive
month-boundary Update calls compound: 1000.75 -> 1000.75*1.00075), `only_province_owner_type_and_matching_resource_id_affected`
(a Country-owned population resource and a Province-owned gold resource both stay untouched),
and `two_provinces_of_same_owner_diverge_independently` (two Province-owned population resources
with different seed values grow to different absolute values from the same percentage).

Gate: `dotnet test src/GlobalStrategy.Core.sln` -> initially failed with a testhost launch error
("You must install or update .NET to run this application" -- `Microsoft.NETCore.App 8.0.0` was
requested but only `10.0.9` is installed on this machine). Setting `DOTNET_ROLL_FORWARD=LatestMajor`
before invoking `dotnet test` resolves this (rolls the net8.0-targeted test projects forward to the
installed net10 runtime) without any project file changes. Full run:
`Passed! - Failed: 0, Passed: 131, Skipped: 0, Total: 131` across ECS.Tests, ECS.Viewer.Tests, and
Game.Tests (the new file's 5 facts included, filtered run also confirmed 5/5 passed standalone).

Gotcha for next iteration: **this repo's `dotnet test` gate requires `DOTNET_ROLL_FORWARD=LatestMajor`
in the environment on this machine** (only .NET 10 SDK/runtime installed, projects target net8.0).
Export it before running any `dotnet test` gate: `export DOTNET_ROLL_FORWARD=LatestMajor` (bash) or
`$env:DOTNET_ROLL_FORWARD = "LatestMajor"` (PowerShell). `dotnet build` gates were unaffected (build
does not launch a testhost), which is why this didn't surface until the first test-gated task.

Also note: `.ralph/prd.md` is CRLF-line-ended -- the Edit tool's string matching (which uses 
)
silently fails to find otherwise-correct-looking multi-line `old_string` blocks copied from Read
output against this file. Worked around by patching the JSON byte-for-byte with a small Python
script (matching 
 explicitly) instead of Edit. Future iterations touching prd.md across
multiple lines should do the same, or restrict Edit to single-line old_strings.

Notes for next iteration: Next task is "Extend InitSystemTests with province population seeding
case" -- in `src/Game.Tests/InitSystemTests.cs`, add `Population` values to `BuildLogic`'s
`provinceConfig` `ProvinceEntry`s, and add `province_population_seeded_from_config": after the
first `GameLogic.Update`, one `Resource{ResourceId=population}` + `ResourceOwner(_, OwnerType.Province)`
entity should exist per `ProvinceEntry`, with `Value == entry.Population` and `OwnerId ==
entry.ProvinceId` (not `entry.CountryId`). Remember to export `DOTNET_ROLL_FORWARD=LatestMajor`
before running the gate.

---

## 2026-07-15 -- Extend InitSystemTests with province population seeding case

Task: "Extend InitSystemTests with province population seeding case" (test).

Change: In `src/Game.Tests/InitSystemTests.cs`, `BuildLogic`'s `provinceConfig` now seeds
`Population = 1234.0` for `prov_a` (Great_Britain) and `Population = 5678.0` for `prov_b`
(France). Added a new fact `province_population_seeded_from_config`: builds the logic, calls
`Update(0f)` once, then iterates `logic.World.GetMatchingArchetypes` for archetypes with both
`ResourceOwner` and `Resource` columns, collecting entries where `OwnerType == OwnerType.Province`
and `ResourceId == "population"` into a `Dictionary<string, double>` keyed by `OwnerId`. Asserts
exactly 2 such entities exist and that `byOwnerId["prov_a"] == 1234.0` /
`byOwnerId["prov_b"] == 5678.0` (i.e. keyed by `ProvinceId`, not `CountryId`).

Gotcha hit again: `InitSystemTests.cs` (like `.ralph/prd.md`) is CRLF-line-ended, and the `Edit`
tool's multi-line `old_string` matching failed silently (`String to replace not found`) against
correct-looking text copied from `Read` output, even after single-line edits worked fine on the
same file. Root cause this time wasn't CRLF vs LF alone -- my first attempt at the multi-line
`old_string`/`new_string` pair also had the wrong tab count for the closing braces (guessed 3/2/1
tabs instead of the file's actual 2/1/0 tabs for method-close/class-close/namespace-close), so even
a byte-exact CRLF Python replace failed on the first try with "expected exactly one match". Fixed
by re-reading the exact trailing bytes via `python3 -c "open(...,'rb').read()[-260:]"` before
constructing the replacement. Takeaway for future multi-line edits on this repo's CRLF files: do a
raw byte dump of the target region first to get indentation exactly right, rather than trusting
`Read`'s line-numbered/tab-expanded output for indentation depth.

Gate: `DOTNET_ROLL_FORWARD=LatestMajor` + `dotnet test src/GlobalStrategy.Core.sln` -> filtered run
(`--filter FullyQualifiedName~InitSystemTests`) passed 6/6 in Game.Tests.dll; full solution run
passed `ECS.Tests.dll` 34/34, `ECS.Viewer.Tests.dll` 16/16, `Game.Tests.dll` 132/132 (up from 131),
0 failures overall.

Notes for next iteration: Next task is "Extend ProvinceOwnershipTests with
ownership-change-preserves-population case" -- in `src/Game.Tests/ProvinceOwnershipTests.cs`, add
`change_owner_does_not_affect_population`: seed via `BuildLogic`, call
`ProvinceOwnershipSystem.ChangeOwner`, then assert the province's population `Resource` entity
(keyed by `provinceId` via `ResourceOwner.OwnerId`) is untouched in value and still present under
the same `provinceId`. Check `ProvinceOwnershipTests.cs`'s existing `BuildLogic` helper first --
it's a separate copy from `InitSystemTests.cs`'s, so it will need its own `Population` values
added to its `ProvinceEntry`s if not already present. Remember to export
`DOTNET_ROLL_FORWARD=LatestMajor` before running the gate, and dump raw bytes before multi-line
Edit attempts on this CRLF file.
