# Ralph PRD — Province Population

Add a per-province `population` `Resource` (seeded from a new pipeline-generated density field on `province_config.json`, owned via a new `OwnerType.Province`) that grows monthly by a global percentage constant in `GameSettings`, via a standalone `ProvincePopulationGrowthSystem` decoupled from the existing gold/`ResourceEffect` pipeline. Data-layer foundation only — no UI, no aggregation, no other system reads population. See `Docs/Specs/46_province-population/`.

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "src-components",
		"description": "Add OwnerType.Province enum case",
		"steps": [
			"In src/Game.Components/OwnerType.cs, add Province as a fourth case after Character"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-configs",
		"description": "Add ProvinceEntry.Population field",
		"steps": [
			"In src/Game.Configs/ProvinceConfig.cs, add public double Population { get; set; } to ProvinceEntry"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-configs-loader",
		"description": "Pass population through Stage 2 ProvinceProcessor",
		"steps": [
			"In src/Game.Configs.Loader/ProvinceProcessor.cs Process, add a GetDoubleProp helper mirroring GetStringProp, returning 0.0 if the property is absent",
			"Read the new population GeoJSON property per feature via GetDoubleProp and set it on the constructed ProvinceEntry",
			"Leave existing countryId cross-validation logic unchanged"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "pipeline-python",
		"description": "Add region lookup and density-range dicts to generate_provinces.py",
		"steps": [
			"Add COUNTRY_REGION (countryId -> region key) near PER_COUNTRY_DENSITY_MULTIPLIER, covering every country in country_config.json, with unmapped countries falling back to a Default region",
			"Add REGION_DENSITY_RANGES (region -> (min_people_per_km2, max_people_per_km2)) including a Default fallback range",
			"Populate plausible 1880-era relative density bands per spec (denser South/East Asia and Western Europe, sparser Northern Europe/Central Asia/interior deserts) -- approximate, not researched real data"
		],
		"gate": ".venv\\Scripts\\python.exe -m py_compile scripts\\generate_provinces.py",
		"passes": true
	},
	{
		"category": "pipeline-python",
		"description": "Sample per-province density using the deterministic per-country RNG",
		"steps": [
			"Create one random.Random(deterministic_seed(country_id)) per country up front in run()'s per-country loop, reusing the existing deterministic_seed helper",
			"Thread that RNG into try_option_c as a parameter instead of the RNG it currently constructs internally; remove the internal rng = random.Random(...) line in try_option_c, keeping the same call sequence for seed placement so existing Voronoi output is unaffected",
			"After the per-country provinces list is finalized (Micro/OptionA/OptionC alike) and assign_province_ids has run, look up COUNTRY_REGION.get(country_id, 'Default') and the matching REGION_DENSITY_RANGES range, then for each province in list order draw density = rng.uniform(*density_range) and stash it as prov['_density']",
			"Add a 'population': None placeholder to each emitted feature's properties dict alongside provinceId/countryId/displayName/generationMethod/compassKey",
			"Update the module docstring's Output property list to include population"
		],
		"gate": ".venv\\Scripts\\python.exe -m py_compile scripts\\generate_provinces.py",
		"passes": true
	},
	{
		"category": "pipeline-python",
		"description": "Compute final population from simplified geometry after mapshaper",
		"steps": [
			"After the npx mapshaper -simplify keep-shapes <pct>% subprocess call completes successfully, reload INTERMEDIATE_PATH from disk (the simplified geometry)",
			"Compute each feature's area in EQUAL_AREA_CRS from that simplified polygon (same gpd.GeoSeries(...).to_crs(...).area technique used elsewhere)",
			"Multiply by the matching province's stashed _density (matched by provinceId) and write the result into that feature's population property",
			"Re-serialize INTERMEDIATE_PATH so the on-disk file's population values are computed from the same final geometry that Stage 2/provinces_1880.json will ship"
		],
		"gate": ".venv\\Scripts\\python.exe -m py_compile scripts\\generate_provinces.py",
		"passes": true
	},
	{
		"category": "src-configs",
		"description": "Add GameSettings.PopulationGrowthPercentPerMonth global constant",
		"steps": [
			"In src/Game.Configs/GameSettings.cs, add public double PopulationGrowthPercentPerMonth { get; set; } = 0.075; alongside StartYear/SpeedMultipliers/DefaultLocale/AutoSaveInterval",
			"Add \"populationGrowthPercentPerMonth\": 0.075 to Assets/Configs/game_settings.json"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-systems",
		"description": "Add ProvincePopulationGrowthSystem",
		"steps": [
			"Create src/Game.Systems/ProvincePopulationGrowthSystem.cs with public const string PopulationResourceId = \"population\"; and public static void Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent)",
			"Compute isMonthBoundary the same way as ResourceSystem/ControlSystem (previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year); return early if not crossed",
			"Iterate world.GetMatchingArchetypes({TypeId<ResourceOwner>.Value, TypeId<Resource>.Value}, null); for each row where owners[i].OwnerType == OwnerType.Province && resources[i].ResourceId == PopulationResourceId, set resources[i].Value *= (1.0 + monthlyGrowthPercent / 100.0) via direct array-index mutation (no lambda, per ecs_patterns.md ref/lambda gotcha)",
			"Do not touch ResourceEffect/ResourceLink/PayType"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-tests",
		"description": "Add ProvincePopulationGrowthSystemTests (growth, isolation, first-tick)",
		"steps": [
			"Create src/Game.Tests/ProvincePopulationGrowthSystemTests.cs mirroring ResourceSystemTests.cs's Jan31/Feb1/Jan1/Jan2 constants and world-building helpers",
			"population_unaffected_within_same_month: Update(world, Jan1, Jan2, 0.075) leaves Resource.Value unchanged",
			"population_grows_by_percent_at_month_boundary: a province-owned Resource{ResourceId=population, Value=1000} with Update(world, Jan31, Feb1, 0.075) becomes 1000.75",
			"growth_compounds_across_multiple_months: two successive month-boundary Update calls compound rather than reset",
			"only_province_owner_type_and_matching_resource_id_affected: a population Resource owned with OwnerType.Country is untouched; a province-owned Resource with ResourceId=gold is untouched",
			"two_provinces_of_same_owner_diverge_independently: two independent population resources with different starting values grow to different absolute values but the same relative percentage",
			"first-tick ordering case: build a GameLogic via the shared harness, call Update once with no elapsed time, and assert the province population Resource.Value still equals the seeded entry.Population (no growth on tick 1)"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "src-main",
		"description": "Seed province population entities at init",
		"steps": [
			"In src/Game.Main/InitSystem.Run, change the ProvinceOwnershipSystem.Seed(world, context.Province.Load()); line to first assign var provinceConfig = context.Province.Load();, call ProvinceOwnershipSystem.Seed(world, provinceConfig);, then add CreateProvincePopulationEntities(world, provinceConfig); right after",
			"Implement static void CreateProvincePopulationEntities(World world, ProvinceConfig config): for each ProvinceEntry, world.Create() an entity with ResourceOwner(entry.ProvinceId, OwnerType.Province) and Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = entry.Population } (mirrors CreateResourceEntities's per-country seeding shape, single resource per province, no ResourceEffect/ResourceLink)"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-tests",
		"description": "Extend InitSystemTests for province population seeding",
		"steps": [
			"In src/Game.Tests/InitSystemTests.cs, add Population values to BuildLogic's provinceConfig ProvinceEntrys",
			"Add province_population_seeded_from_config: after the first GameLogic.Update, one Resource{ResourceId=population} + ResourceOwner(_, OwnerType.Province) entity exists per ProvinceEntry, Value == entry.Population, OwnerId == entry.ProvinceId (not entry.CountryId)"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "src-main",
		"description": "Wire ProvincePopulationGrowthSystem into GameLogic.Update",
		"steps": [
			"In src/Game.Main/GameLogic.cs, add a readonly double _populationGrowthPercent; field, set it from settings.PopulationGrowthPercentPerMonth in the constructor (same place _speedMultipliers is captured from settings)",
			"In Update, immediately after OpinionSystem.Update(_world, _previousTime, currentTime);, add ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime, _populationGrowthPercent);"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src-tests",
		"description": "Extend ProvinceOwnershipTests: ownership change does not affect population",
		"steps": [
			"In src/Game.Tests/ProvinceOwnershipTests.cs, add change_owner_does_not_affect_population: seed via BuildLogic, call ProvinceOwnershipSystem.ChangeOwner, then assert the province's population Resource entity (keyed by provinceId via ResourceOwner.OwnerId) is untouched in value and still present under the same provinceId"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "src-tests",
		"description": "Extend SaveLoadRoundTripTests for grown population persistence",
		"steps": [
			"In src/Game.Tests/SaveLoadRoundTripTests.cs, add a case that advances at least one month boundary (growing population), saves via SaveSystem.BuildSnapshot, reloads via LoadSystem.Apply",
			"Assert the grown (not seed) value survives and a subsequent month-boundary Update continues compounding from the persisted value"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": true
	},
	{
		"category": "src-tests",
		"description": "Extend ProvinceProcessorTests for population field extraction",
		"steps": [
			"In src/Game.Tests/ProvinceProcessorTests.cs, add process_extracts_population_field: a feature with a population property round-trips into ProvinceEntry.Population",
			"A feature missing the property defaults to 0.0 (no crash)"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "docs",
		"description": "Update province_config_generator.md rule doc for the new population property",
		"steps": [
			"Check whether .claude/rules/unity/province_config_generator.md's Stage 1/Stage 2 field lists need a one-line mention of the new population property",
			"Documentation currency only -- no behavior change to the doc's described pipeline mechanics"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "unity-manual",
		"description": "Rebuild Core DLLs and confirm clean Unity console",
		"steps": [
			"Run dotnet build src/GlobalStrategy.Core.sln -c Release so Assets/Plugins/Core/ picks up OwnerType.Province, ProvincePopulationGrowthSystem, the updated ProvinceConfig/GameSettings, and the InitSystem/GameLogic wiring",
			"Let Unity finish its domain reload (refresh_unity) and confirm read_console(types=[\"error\"]) is clean"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release -- then refresh_unity and read_console(types=[\"error\"]) must report no errors",
		"passes": false
	},
	{
		"category": "pipeline-python",
		"description": "Re-run the province generation pipeline with real geometry and regenerate configs",
		"steps": [
			"Run .venv\\Scripts\\python.exe scripts\\generate_provinces.py from the project root",
			"Confirm the script's summary output shows the same per-method country counts as before this change (Micro/OptionA/OptionC counts unaffected -- only a population property was added) and no new warnings appear",
			"Re-run the Stage 2 C# loader (Game.Configs.Loader, via its existing loader_config.json-driven entry point) to regenerate Assets/Configs/province_config.json with the new population field populated for every entry",
			"Cross-check every province's countryId still exists in country_config.json per .claude/rules/config_validation.md, and that every province_config.json entry has a nonzero population value"
		],
		"gate": ".venv\\Scripts\\python.exe scripts\\generate_provinces.py -- then re-run the Stage 2 loader and verify province_config.json entries all have countryId present in country_config.json and population > 0",
		"passes": false
	}
]
```
</content>
