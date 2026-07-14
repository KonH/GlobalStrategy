# Ralph PRD — Province Population

Add a per-province `population` `Resource` (owned via a new `OwnerType.Province`), seeded at init from a new pipeline-generated field on `province_config.json` (region-density lookup × province polygon area), and grown monthly by a standalone `ProvincePopulationGrowthSystem` reading a new `GameSettings.PopulationGrowthPercentPerMonth` constant — without touching the existing gold/income `ResourceEffect` pipeline. Spec: `Docs/Specs/46_province-population/spec.md`. Plan: `Docs/Specs/46_province-population/plan.md`.

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "src",
		"description": "Add OwnerType.Province enum case",
		"steps": [
			"In src/Game.Components/OwnerType.cs, add Province as a fourth case after Character"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src",
		"description": "Add ProvinceEntry.Population field",
		"steps": [
			"In src/Game.Configs/ProvinceConfig.cs, add public double Population { get; set; } to ProvinceEntry"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src",
		"description": "Pass population through Stage 2 ProvinceProcessor",
		"steps": [
			"In src/Game.Configs.Loader/ProvinceProcessor.cs Process, add a GetDoubleProp helper mirroring GetStringProp (returns 0.0 if absent)",
			"Read the new population property per feature and set it on the constructed ProvinceEntry",
			"Do not change countryId cross-validation behavior"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "config",
		"description": "Add GameSettings.PopulationGrowthPercentPerMonth",
		"steps": [
			"In src/Game.Configs/GameSettings.cs, add public double PopulationGrowthPercentPerMonth { get; set; } = 0.075; alongside StartYear/SpeedMultipliers/DefaultLocale/AutoSaveInterval",
			"Add \"populationGrowthPercentPerMonth\": 0.075 to Assets/Configs/game_settings.json"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src",
		"description": "Add ProvincePopulationGrowthSystem",
		"steps": [
			"Create src/Game.Systems/ProvincePopulationGrowthSystem.cs with public const string PopulationResourceId = \"population\";",
			"Add public static void Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent)",
			"Compute isMonthBoundary the same way as ResourceSystem/ControlSystem; return early if not crossed",
			"Iterate world.GetMatchingArchetypes({TypeId<ResourceOwner>.Value, TypeId<Resource>.Value}, null), and for rows where OwnerType == OwnerType.Province && ResourceId == PopulationResourceId, set Value *= (1.0 + monthlyGrowthPercent / 100.0) via direct array-index mutation (no lambda, per ecs_patterns.md)",
			"Do not touch ResourceEffect/ResourceLink/PayType"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src",
		"description": "Seed province population entities at init",
		"steps": [
			"In src/Game.Main/InitSystem.Run, capture var provinceConfig = context.Province.Load(); then call ProvinceOwnershipSystem.Seed(world, provinceConfig);",
			"Add a new CreateProvincePopulationEntities(world, provinceConfig); call right after",
			"Implement static void CreateProvincePopulationEntities(World world, ProvinceConfig config): for each ProvinceEntry, world.Create() an entity with ResourceOwner(entry.ProvinceId, OwnerType.Province) and Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = entry.Population }"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": true
	},
	{
		"category": "src",
		"description": "Wire population growth into GameLogic.Update",
		"steps": [
			"In src/Game.Main/GameLogic.cs, add readonly double _populationGrowthPercent; field, set it from settings.PopulationGrowthPercentPerMonth in the constructor (same place _speedMultipliers is captured)",
			"In Update, immediately after OpinionSystem.Update(_world, _previousTime, currentTime);, add ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime, _populationGrowthPercent);"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "test",
		"description": "Add ProvincePopulationGrowthSystemTests",
		"steps": [
			"Create src/Game.Tests/ProvincePopulationGrowthSystemTests.cs mirroring ResourceSystemTests.cs's Jan31/Feb1/Jan1/Jan2 constants and world-building helpers",
			"population_unaffected_within_same_month: Update(world, Jan1, Jan2, 0.075) leaves Resource.Value unchanged",
			"population_grows_by_percent_at_month_boundary: a province-owned Resource{ResourceId=population, Value=1000} with Update(world, Jan31, Feb1, 0.075) becomes 1000.75",
			"growth_compounds_across_multiple_months: two successive month-boundary Update calls compound rather than reset",
			"only_province_owner_type_and_matching_resource_id_affected: OwnerType.Country-owned population resource untouched; province-owned non-population resource (e.g. gold) untouched",
			"two_provinces_of_same_owner_diverge_independently: two independent population resources with different starting values grow to different absolute values at the same relative percentage"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "test",
		"description": "Extend InitSystemTests with province population seeding case",
		"steps": [
			"In src/Game.Tests/InitSystemTests.cs, add Population values to BuildLogic's provinceConfig ProvinceEntrys",
			"Add province_population_seeded_from_config: after the first GameLogic.Update, one Resource{ResourceId=population} + ResourceOwner(_, OwnerType.Province) entity exists per ProvinceEntry, Value == entry.Population, OwnerId == entry.ProvinceId (not entry.CountryId)"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "test",
		"description": "Extend ProvinceOwnershipTests with ownership-change-preserves-population case",
		"steps": [
			"In src/Game.Tests/ProvinceOwnershipTests.cs, add change_owner_does_not_affect_population",
			"Seed via BuildLogic, call ProvinceOwnershipSystem.ChangeOwner, then assert the province's population Resource entity (keyed by provinceId via ResourceOwner.OwnerId) is untouched in value and still present under the same provinceId"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "test",
		"description": "Extend SaveLoadRoundTripTests with population persistence case",
		"steps": [
			"In src/Game.Tests/SaveLoadRoundTripTests.cs, add a case that advances at least one month boundary (growing population)",
			"Save via SaveSystem.BuildSnapshot, reload via LoadSystem.Apply",
			"Assert the grown (not seed) value survives and a subsequent month-boundary Update continues compounding from the persisted value"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "test",
		"description": "Extend ProvinceProcessorTests with population field extraction case",
		"steps": [
			"In src/Game.Tests/ProvinceProcessorTests.cs, add process_extracts_population_field",
			"A feature with a population property round-trips into ProvinceEntry.Population",
			"A feature missing the property defaults to 0.0 (no crash)"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "test",
		"description": "Add first-tick no-growth ordering test",
		"steps": [
			"Add a new case in ProvincePopulationGrowthSystemTests.cs (or alongside GameLogicOrgTests.cs-style tests) confirming no growth is applied on the very first GameLogic.Update call",
			"Build a GameLogic via the shared harness, call Update once with no elapsed time/no multiplier change, and assert the province population Resource.Value still equals the seeded entry.Population"
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "unity-manual",
		"description": "Update province_config_generator.md rule doc for new population field",
		"steps": [
			"Check whether .claude/rules/unity/province_config_generator.md's Stage 1/Stage 2 field lists need a one-line mention of the new population property",
			"Documentation currency only; no behavior change to the doc's described pipeline mechanics",
			"needs manual visual check"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "src",
		"description": "Rebuild Core DLLs and confirm clean Unity console",
		"steps": [
			"After src/ changes compile and tests pass, run dotnet build src/GlobalStrategy.Core.sln -c Release so Assets/Plugins/Core/ picks up OwnerType.Province, ProvincePopulationGrowthSystem, the updated ProvinceConfig/GameSettings, and the InitSystem/GameLogic wiring",
			"Let Unity finish its domain reload"
		],
		"gate": "refresh_unity then read_console(types=[\"error\"]) reports no errors",
		"passes": false
	},
	{
		"category": "pipeline",
		"description": "Add region lookup + density ranges to generate_provinces.py",
		"steps": [
			"Add two module-level dicts near PER_COUNTRY_DENSITY_MULTIPLIER: COUNTRY_REGION (countryId -> region key, covering every country in country_config.json; unmapped countries fall back to \"Default\") and REGION_DENSITY_RANGES (region -> (min_people_per_km2, max_people_per_km2)), with a \"Default\" entry as the fallback range",
			"Populate plausible 1880-era relative density bands per spec (denser South/East Asia and Western Europe, sparser Northern Europe/Central Asia/interior deserts) — approximate, not researched real data"
		],
		"gate": ".venv\\Scripts\\python.exe -c \"import scripts.generate_provinces as g; assert 'Default' in g.COUNTRY_REGION.values() or True; assert 'Default' in g.REGION_DENSITY_RANGES\"",
		"passes": false
	},
	{
		"category": "pipeline",
		"description": "Sample density and attach population per province in generate_provinces.py",
		"steps": [
			"Create one random.Random(deterministic_seed(country_id)) per country up front (reuse the existing deterministic_seed helper) and thread it into try_option_c as a parameter instead of the RNG it currently constructs internally (remove the internal rng = random.Random(...) line in try_option_c, keeping the same call sequence for seed placement so existing Voronoi output is unaffected)",
			"After the per-country provinces list is finalized (Micro/OptionA/OptionC alike) and assign_province_ids has run, look up the country's region via COUNTRY_REGION.get(country_id, \"Default\") and its density range via REGION_DENSITY_RANGES",
			"For each province in list order draw density = rng.uniform(*density_range) and stash it as prov[\"_density\"] (not yet multiplied by area)",
			"Add a \"population\": None placeholder to each emitted feature's properties dict, alongside provinceId/countryId/displayName/generationMethod/compassKey",
			"Update the module docstring's Output property list to include population"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "pipeline",
		"description": "Compute final population from simplified geometry after mapshaper step",
		"steps": [
			"After the npx mapshaper -simplify keep-shapes <pct>% subprocess call completes successfully, reload INTERMEDIATE_PATH from disk (the simplified geometry)",
			"Compute each feature's area in EQUAL_AREA_CRS from that simplified polygon (same gpd.GeoSeries(...).to_crs(...).area technique used elsewhere)",
			"Multiply by the matching province's stashed _density (matched by provinceId), write the result into that feature's population property",
			"Re-serialize INTERMEDIATE_PATH so the on-disk file's population values are computed from the same final geometry that Stage 2/provinces_1880.json will ship"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "pipeline",
		"description": "Re-run the province generation pipeline with real geometry and regenerate province_config.json",
		"steps": [
			"Run scripts/generate_provinces.py from the project root (.venv\\Scripts\\python.exe scripts\\generate_provinces.py)",
			"Confirm the script's summary output shows the same per-method country counts as before this change (Micro/OptionA/OptionC counts unaffected — only a population property was added) and that no new warnings appear",
			"Re-run the Stage 2 C# loader (Game.Configs.Loader, via its existing loader_config.json-driven entry point) to regenerate Assets/Configs/province_config.json with the new population field populated for every entry"
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	}
]
```
</content>
