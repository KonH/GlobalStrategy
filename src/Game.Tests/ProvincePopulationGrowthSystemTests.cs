using System;
using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class ProvincePopulationGrowthSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1, 0, 0, 0);
		static readonly DateTime Jan2 = new DateTime(1880, 1, 2, 0, 0, 0);

		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static World CreateWorldWithProvincePopulation(string provinceId, double initialValue, out int entity) {
			var world = new World();
			entity = world.Create();
			world.Add(entity, new ResourceOwner(provinceId, OwnerType.Province));
			world.Add(entity, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = initialValue });
			return world;
		}

		[Fact]
		void population_unaffected_within_same_month() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int e);
			ProvincePopulationGrowthSystem.Update(world, Jan1, Jan2, 0.075);
			Assert.Equal(1000.0, world.Get<Resource>(e).Value);
		}

		[Fact]
		void population_grows_by_percent_at_month_boundary() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int e);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			Assert.Equal(1000.75, world.Get<Resource>(e).Value, 6);
		}

		[Fact]
		void growth_compounds_across_multiple_months() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int e);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			double afterFirst = world.Get<Resource>(e).Value;
			Assert.Equal(1000.75, afterFirst, 6);

			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ProvincePopulationGrowthSystem.Update(world, feb28, mar1, 0.075);
			double afterSecond = world.Get<Resource>(e).Value;
			Assert.Equal(afterFirst * 1.00075, afterSecond, 6);
			Assert.NotEqual(afterFirst, afterSecond);
		}

		[Fact]
		void only_province_owner_type_and_matching_resource_id_affected() {
			var world = new World();

			int countryPopEntity = world.Create();
			world.Add(countryPopEntity, new ResourceOwner("Great_Britain", OwnerType.Country));
			world.Add(countryPopEntity, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 500.0 });

			int provinceGoldEntity = world.Create();
			world.Add(provinceGoldEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provinceGoldEntity, new Resource { ResourceId = "gold", Value = 200.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(500.0, world.Get<Resource>(countryPopEntity).Value);
			Assert.Equal(200.0, world.Get<Resource>(provinceGoldEntity).Value);
		}

		[Fact]
		void two_provinces_of_same_owner_diverge_independently() {
			var world = new World();

			int e1 = world.Create();
			world.Add(e1, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(e1, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 1000.0 });

			int e2 = world.Create();
			world.Add(e2, new ResourceOwner("prov_b", OwnerType.Province));
			world.Add(e2, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 2000.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			double v1 = world.Get<Resource>(e1).Value;
			double v2 = world.Get<Resource>(e2).Value;

			Assert.NotEqual(v1, v2);
			Assert.Equal(1000.0 * 1.00075, v1, 6);
			Assert.Equal(2000.0 * 1.00075, v2, 6);
		}

		static GameLogic BuildLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly",
				PopulationGrowthPercentPerMonth = 0.075
			};
			var resourceConfig = new ResourceConfig { Resources = new List<ResourceDefinition>() };
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain", Population = 1000.0 },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = "France", Population = 2000.0 }
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialPlayerCountryId: "Great_Britain",
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(provinceConfig));
			return new GameLogic(ctx);
		}

		[Fact]
		void first_tick_does_not_grow_seeded_population() {
			var logic = BuildLogic();
			logic.Update(0f);
			var world = logic.World;

			double? popValue = null;
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == "prov_a" && owners[i].OwnerType == OwnerType.Province
						&& resources[i].ResourceId == ProvincePopulationGrowthSystem.PopulationResourceId) {
						popValue = resources[i].Value;
					}
				}
			}

			Assert.Equal(1000.0, popValue);
		}
	}
}
