# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-15 — Add OwnerType.Province enum case

Task: `src-components` / "Add OwnerType.Province enum case" (first task in `.ralph/prd.md`).

Changes:
- `src/Game.Components/OwnerType.cs`: added `Province` as a fourth case after `Character`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Notes for next iteration: the next task ("Add ProvinceEntry.Population field" in `src/Game.Configs/ProvinceConfig.cs`) is independent of this one and can proceed directly.

---

## 2026-07-15 — Add ProvinceEntry.Population field

Task: `src-configs` / "Add ProvinceEntry.Population field" (second task in `.ralph/prd.md`).

Changes:
- `src/Game.Configs/ProvinceConfig.cs`: added `public double Population { get; set; }` to `ProvinceEntry`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Notes for next iteration: the next task ("Pass population through Stage 2 ProvinceProcessor" in `src/Game.Configs.Loader/ProvinceProcessor.cs`) needs a `GetDoubleProp` helper mirroring the existing `GetStringProp`; check that file's existing structure before adding.

---

## 2026-07-15 — Pass population through Stage 2 ProvinceProcessor

Task: `src-configs-loader` / "Pass population through Stage 2 ProvinceProcessor" (third task in `.ralph/prd.md`).

Changes:
- `src/Game.Configs.Loader/ProvinceProcessor.cs`: added a `GetDoubleProp(JsonNode? props, string key)` helper mirroring `GetStringProp`, returning `0.0` when the property is absent. Read the `population` GeoJSON property per feature via `GetDoubleProp` and set it on the constructed `ProvinceEntry.Population`. Existing `countryId` cross-validation logic untouched.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Notes for next iteration: the next task ("Add region lookup and density-range dicts to generate_provinces.py") is Python pipeline work in `scripts/generate_provinces.py` — read the existing `PER_COUNTRY_DENSITY_MULTIPLIER` dict and `country_config.json` country list first to build `COUNTRY_REGION` covering every country.

---

## 2026-07-15 — Add region lookup and density-range dicts to generate_provinces.py

Task: `pipeline-python` / "Add region lookup and density-range dicts to generate_provinces.py" (fourth task in `.ralph/prd.md`).

Changes:
- `scripts/generate_provinces.py`: added `COUNTRY_REGION` (countryId -> region key) mapping all 154 countries from `Assets/Configs/country_config.json` into 16 broad regions (WesternEurope, NorthernEurope, EasternEurope, Russia, SouthAsia, EastAsia, SoutheastAsia, MiddleEast, NorthAfrica, SubSaharanAfrica, CentralAsia, NorthAmerica, CentralAmerica, Caribbean, SouthAmerica, Oceania). Added `REGION_DENSITY_RANGES` (region -> (min, max) people/km²) with a `"Default"` fallback range, both placed near `PER_COUNTRY_DENSITY_MULTIPLIER`. Density bands are approximate 1880-era relative guesses (denser Western Europe/South Asia/East Asia, sparser Northern Europe/Central Asia/Russia/deserts/Oceania), not researched real data.

Verification beyond the gate: ran a one-off check confirming all 154 `country_config.json` countryIds are present in `COUNTRY_REGION` (no unmapped, no extras) and every region value used has a matching `REGION_DENSITY_RANGES` entry.

Gate: `.venv\Scripts\python.exe -m py_compile scripts\generate_provinces.py` → exited cleanly, no output (compiles OK).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Notes for next iteration: the next task ("Sample per-province density using the deterministic per-country RNG") needs to move `try_option_c`'s internal `rng = random.Random(deterministic_seed(country_id))` out to the per-country loop in `run()` and thread it in as a parameter, then use `COUNTRY_REGION`/`REGION_DENSITY_RANGES` (just added) to sample a `_density` per province after `assign_province_ids` runs. Also add a `"population": None` placeholder to each feature's properties dict and update the module docstring's Output property list.

---

## 2026-07-15 — Sample per-province density using the deterministic per-country RNG

Task: `pipeline-python` / "Sample per-province density using the deterministic per-country RNG" (fifth task in `.ralph/prd.md`).

Changes:
- `scripts/generate_provinces.py`: promoted `import random` to a top-level module import (was previously a local import inside `try_option_c`). `try_option_c` now takes `rng` as a parameter instead of constructing its own `random.Random(deterministic_seed(country_id))` internally — the seed placement and call sequence into `seed_points_in_polygon` is unchanged, so existing Voronoi output is unaffected. In `run()`'s per-country loop, one `rng = random.Random(deterministic_seed(country_id))` is now created up front (before the Micro/OptionA/OptionC branch) and passed into `try_option_c`. After `provinces = assign_province_ids(country_id, provinces)`, look up `REGION_DENSITY_RANGES[COUNTRY_REGION.get(country_id, "Default")]` and, for each province in list order, draw `prov["_density"] = rng.uniform(*density_range)`. Added a `"population": None` placeholder to each emitted feature's properties dict (alongside provinceId/countryId/displayName/generationMethod/compassKey). Updated the module docstring's Output property list to mention `population`.

Gate: `.venv\Scripts\python.exe -m py_compile scripts\generate_provinces.py` → exited cleanly, no output (compiles OK).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Notes for next iteration: the next task ("Compute final population from simplified geometry after mapshaper") runs after the `npx mapshaper` subprocess call succeeds — it must reload `INTERMEDIATE_PATH` from disk, recompute each feature's area in `EQUAL_AREA_CRS` from the simplified geometry, multiply by the matching province's `_density` (matched by `provinceId` — note `_density` is stashed on the in-memory `prov` dicts inside the per-country loop, not on `all_features`, so the reload step needs to build a `provinceId -> _density` lookup across all countries before doing the area x density multiply), write the result into `population`, and re-serialize `INTERMEDIATE_PATH`.

---

## 2026-07-15 — Compute final population from simplified geometry after mapshaper

Task: `pipeline-python` / "Compute final population from simplified geometry after mapshaper" (sixth task in `.ralph/prd.md`).

Changes:
- `scripts/generate_provinces.py`: added a `density_by_province_id` dict built alongside the existing per-province `_density` stashing in the main per-country loop (`density_by_province_id[prov["provinceId"]] = prov["_density"]`), so the density value survives past the loop where `prov` dicts go out of scope. After the `npx mapshaper` simplify subprocess call succeeds, reload `INTERMEDIATE_PATH` from disk via `json.load`, and for each feature compute its area in `EQUAL_AREA_CRS` from the simplified geometry (`shape(feature["geometry"])` → `gpd.GeoSeries(..., crs=WGS84_CRS).to_crs(EQUAL_AREA_CRS).area`), multiply by `density_by_province_id[province_id]`, and write the result into `feature["properties"]["population"]`. Re-serialize `INTERMEDIATE_PATH` with the updated population values, then rebind `all_features = simplified_collection["features"]` so the subsequent `update_province_locales(all_features)` call (which only reads `properties`, not geometry) operates on the same reloaded feature list.

Gate: `.venv\Scripts\python.exe -m py_compile scripts\generate_provinces.py` → exited cleanly, no output (compiles OK).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Note: the `Edit` tool failed to match the `"passes": false` → `true` replacement for this task despite visually-identical text (likely an invisible whitespace/encoding quirk in that region of the file) — worked around it with a direct Python line-index replacement instead, verified by re-reading the file afterward.

Notes for next iteration: the next task ("Add GameSettings.PopulationGrowthPercentPerMonth global constant") is independent C# config work — add the property to `src/Game.Configs/GameSettings.cs` and a matching key to `Assets/Configs/game_settings.json`.

---

## 2026-07-15 — Add GameSettings.PopulationGrowthPercentPerMonth global constant

Task: `src-configs` / "Add GameSettings.PopulationGrowthPercentPerMonth global constant" (seventh task in `.ralph/prd.md`).

Changes:
- `src/Game.Configs/GameSettings.cs`: added `public double PopulationGrowthPercentPerMonth { get; set; } = 0.075;` alongside `StartYear`/`SpeedMultipliers`/`DefaultLocale`/`AutoSaveInterval`.
- `Assets/Configs/game_settings.json`: added `"populationGrowthPercentPerMonth": 0.075`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md`.

Note: the `Edit` tool again failed to match the `"passes": false` → `true` replacement for this task's block (same whitespace/encoding quirk noted in the previous entry) — worked around it with a direct Python line-index replacement (line 88), verified afterward via grep.

Notes for next iteration: the next task ("Add ProvincePopulationGrowthSystem") creates `src/Game.Systems/ProvincePopulationGrowthSystem.cs` — check `ResourceSystem`/`ControlSystem` for the existing month-boundary detection pattern and `ecs_patterns.md`'s ref/lambda gotcha (use `AsSpan()`/direct array-index mutation, not a lambda, when mutating matched archetype rows).

---

## 2026-07-15 — Add ProvincePopulationGrowthSystem

Task: `src-systems` / "Add ProvincePopulationGrowthSystem" (eighth task in `.ralph/prd.md`).

Changes:
- `src/Game.Systems/ProvincePopulationGrowthSystem.cs` (new file): `public const string PopulationResourceId = "population";` and `public static void Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent)`. Computes `isMonthBoundary` the same way as `ResourceSystem`/`ControlSystem` and returns early if not crossed. Iterates `world.GetMatchingArchetypes({TypeId<ResourceOwner>.Value, TypeId<Resource>.Value}, null)` and, for rows where `owners[i].OwnerType == OwnerType.Province && resources[i].ResourceId == PopulationResourceId`, mutates `resources[i].Value *= (1.0 + monthlyGrowthPercent / 100.0)` via direct array-index assignment (no lambda, no `ref` local, per `ecs_patterns.md`). Does not touch `ResourceEffect`/`ResourceLink`/`PayType`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).

Flipped this task's `"passes"` to `true` in `.ralph/prd.md` (used a direct line-index Python replacement again — the `Edit` tool's exact-string match against this JSON block continues to fail for reasons not yet diagnosed; a plain grep -n confirms the correct line number before each replacement).

Notes for next iteration: the next task ("Add ProvincePopulationGrowthSystemTests") creates `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` mirroring `ResourceSystemTests.cs`'s Jan31/Feb1/Jan1/Jan2 constants and world-building helpers. `ProvincePopulationGrowthSystem.Update` and `PopulationResourceId` (just added) are the only new public surface it needs; `ResourceOwner`/`Resource`/`OwnerType.Province` are already available.

---

## 2026-07-15 — Add ProvincePopulationGrowthSystemTests (BLOCKED on gate)

Task: `src-tests` / "Add ProvincePopulationGrowthSystemTests (growth, isolation, first-tick)" (ninth task in `.ralph/prd.md`).

Changes:
- `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` (new file): mirrors `ResourceSystemTests.cs`'s Jan31/Feb1/Jan1/Jan2 constants and a `CreateWorldWithPopulation` helper (parallel to `CreateWorldWithResource`, but seeds `ResourceOwner(provinceId, OwnerType.Province)` + a `population`-id `Resource`). Implemented five of the six requested cases: `population_unaffected_within_same_month`, `population_grows_by_percent_at_month_boundary`, `growth_compounds_across_multiple_months`, `only_province_owner_type_and_matching_resource_id_affected` (country-owned population untouched; province-owned gold untouched), `two_provinces_of_same_owner_diverge_independently`.
- Deliberately **did not** implement the sixth case, "first-tick ordering case: build a GameLogic via the shared harness ... assert the province population Resource.Value still equals the seeded entry.Population" — `InitSystem` does not yet call `CreateProvincePopulationEntities` (that's task 10, "Seed province population entities at init", still `passes: false`) and `GameLogic.Update` does not yet call `ProvincePopulationGrowthSystem.Update` (task 12, still `passes: false`). A `GameLogic`-harness test today would find zero province-population entities, so it can't meaningfully assert the described behavior yet. Task 11 ("Extend InitSystemTests for province population seeding") already covers the identical scenario once tasks 10/12 land, so this sixth case is deferred there rather than duplicated.

Gate attempted: `dotnet test src/GlobalStrategy.Core.sln` → **environment blocker, unrelated to this change**. The machine has only the **.NET 10 SDK/runtime installed** (`dotnet --list-sdks` → `10.0.301`; `dotnet --list-runtimes` → only `10.0.9` entries for `Microsoft.NETCore.App`/`AspNetCore.App`/`WindowsDesktop.App`). Every `net8.0`-targeted test project in the solution (`Game.Tests`, `ECS.Viewer.Tests`, `ECS.Tests`) fails to launch its testhost with `You must install or update .NET to run this application. ... Framework: 'Microsoft.NETCore.App', version '8.0.0' (x64)`. `dotnet build` (the gate for all prior tasks) only needs the SDK and doesn't hit this, which is why it hasn't surfaced until now — this is the first task in the PRD whose gate is `dotnet test`. Confirmed no alternate .NET 8 runtime install exists anywhere on disk (`find / -iname "*dotnet*"` only shows the single `C:\Program Files\dotnet` with `10.0.9`), and no `global.json` pins/redirects the SDK.
- Did **not** work around this by adding `<RollForward>LatestMajor</RollForward>` or retargeting `Game.Tests.csproj`/other `net8.0` projects to a version present on this machine — that's a build-configuration change with consequences beyond this task's scope (`Game.Configs.Loader`, a `net8.0` executable, is the Stage 2 pipeline tool invoked outside this repo's test harness; retargeting it isn't something to do speculatively without user confirmation).

`.ralph/prd.md`: task left at `"passes": false` (gate did not pass — could not even execute, let alone confirm green).

**Blocker for the next iteration and beyond:** essentially every remaining task in the PRD gates on `dotnet test src/GlobalStrategy.Core.sln`, which cannot run at all until either (a) a .NET 8.0 runtime is installed on this machine, or (b) the user approves retargeting/roll-forward for the affected `net8.0` projects. This needs a human decision before the loop can make further progress on any test-gated task — flagging rather than guessing.
