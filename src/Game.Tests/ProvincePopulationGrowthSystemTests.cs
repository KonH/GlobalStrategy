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

		static World CreateWorldWithResource(string ownerId, OwnerType ownerType, string resourceId,
			double initialValue, out int resourceEntity) {
			var world = new World();
			resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(ownerId, ownerType));
			world.Add(resourceEntity, new Resource { ResourceId = resourceId, Value = initialValue });
			return world;
		}

		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static GameLogic BuildGameLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true }
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
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain", Population = 1234.0 }
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

		static double GetProvincePopulation(World world, string provinceId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var owners = arch.GetColumn<ResourceOwner>();
				var resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerType == OwnerType.Province && owners[i].OwnerId == provinceId &&
						resources[i].ResourceId == ProvincePopulationGrowthSystem.PopulationResourceId) {
						return resources[i].Value;
					}
				}
			}
			throw new InvalidOperationException($"No population resource found for province '{provinceId}'.");
		}

		[Fact]
		void first_tick_does_not_apply_growth() {
			var logic = BuildGameLogic();
			logic.Update(0f);
			Assert.Equal(1234.0, GetProvincePopulation(logic.World, "prov_a"));
		}

		[Fact]
		void population_unaffected_within_same_month() {
			var world = CreateWorldWithResource("province_a", OwnerType.Province,
				ProvincePopulationGrowthSystem.PopulationResourceId, 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan1, Jan2, 0.075);
			Assert.Equal(1000.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void population_grows_by_percent_at_month_boundary() {
			var world = CreateWorldWithResource("province_a", OwnerType.Province,
				ProvincePopulationGrowthSystem.PopulationResourceId, 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			Assert.Equal(1000.75, world.Get<Resource>(re).Value, 6);
		}

		[Fact]
		void growth_compounds_across_multiple_months() {
			var world = CreateWorldWithResource("province_a", OwnerType.Province,
				ProvincePopulationGrowthSystem.PopulationResourceId, 1000.0, out int re);

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			Assert.Equal(1000.75, world.Get<Resource>(re).Value, 6);

			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ProvincePopulationGrowthSystem.Update(world, feb28, mar1, 0.075);
			Assert.Equal(1000.75 * 1.00075, world.Get<Resource>(re).Value, 6);
		}

		[Fact]
		void only_province_owner_type_and_matching_resource_id_affected() {
			var world = new World();

			int countryPopEntity = world.Create();
			world.Add(countryPopEntity, new ResourceOwner("Russia", OwnerType.Country));
			world.Add(countryPopEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 500.0
			});

			int provinceGoldEntity = world.Create();
			world.Add(provinceGoldEntity, new ResourceOwner("province_a", OwnerType.Province));
			world.Add(provinceGoldEntity, new Resource { ResourceId = "gold", Value = 300.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(500.0, world.Get<Resource>(countryPopEntity).Value);
			Assert.Equal(300.0, world.Get<Resource>(provinceGoldEntity).Value);
		}

		[Fact]
		void two_provinces_of_same_owner_diverge_independently() {
			var world = new World();

			int re1 = world.Create();
			world.Add(re1, new ResourceOwner("province_a", OwnerType.Province));
			world.Add(re1, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 1000.0 });

			int re2 = world.Create();
			world.Add(re2, new ResourceOwner("province_b", OwnerType.Province));
			world.Add(re2, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 2000.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(1000.75, world.Get<Resource>(re1).Value, 6);
			Assert.Equal(2001.5, world.Get<Resource>(re2).Value, 6);
		}
	}
}
