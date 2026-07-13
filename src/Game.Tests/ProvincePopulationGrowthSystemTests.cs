using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ProvincePopulationGrowthSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1, 0, 0, 0);
		static readonly DateTime Jan2 = new DateTime(1880, 1, 2, 0, 0, 0);

		static World CreateWorldWithProvincePopulation(string provinceId, double initialValue, out int resourceEntity) {
			var world = new World();
			resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(provinceId, OwnerType.Province));
			world.Add(resourceEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = initialValue
			});
			return world;
		}

		[Fact]
		void population_unaffected_within_same_month() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan1, Jan2, 0.075);
			Assert.Equal(1000.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void population_grows_by_percent_at_month_boundary() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			Assert.Equal(1000.75, world.Get<Resource>(re).Value, 6);
		}

		[Fact]
		void growth_compounds_across_multiple_months() {
			var world = CreateWorldWithProvincePopulation("prov_a", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			double afterFirst = world.Get<Resource>(re).Value;
			Assert.Equal(1000.75, afterFirst, 6);

			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ProvincePopulationGrowthSystem.Update(world, feb28, mar1, 0.075);
			double afterSecond = world.Get<Resource>(re).Value;
			Assert.Equal(afterFirst * 1.00075, afterSecond, 6);
			Assert.NotEqual(afterFirst, afterSecond);
		}

		[Fact]
		void only_province_owner_type_and_matching_resource_id_affected() {
			var world = new World();

			int countryPopEntity = world.Create();
			world.Add(countryPopEntity, new ResourceOwner("Great_Britain", OwnerType.Country));
			world.Add(countryPopEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 500.0
			});

			int provinceGoldEntity = world.Create();
			world.Add(provinceGoldEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provinceGoldEntity, new Resource { ResourceId = "gold", Value = 200.0 });

			int provincePopEntity = world.Create();
			world.Add(provincePopEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provincePopEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 1000.0
			});

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(500.0, world.Get<Resource>(countryPopEntity).Value);
			Assert.Equal(200.0, world.Get<Resource>(provinceGoldEntity).Value);
			Assert.Equal(1000.75, world.Get<Resource>(provincePopEntity).Value, 6);
		}

		[Fact]
		void two_provinces_of_same_owner_diverge_independently() {
			var world = new World();

			int provAEntity = world.Create();
			world.Add(provAEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provAEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 1000.0
			});

			int provBEntity = world.Create();
			world.Add(provBEntity, new ResourceOwner("prov_b", OwnerType.Province));
			world.Add(provBEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 2000.0
			});

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			double aValue = world.Get<Resource>(provAEntity).Value;
			double bValue = world.Get<Resource>(provBEntity).Value;

			Assert.Equal(1000.0 * 1.00075, aValue, 6);
			Assert.Equal(2000.0 * 1.00075, bValue, 6);
			Assert.NotEqual(aValue, bValue);
		}
	}
}
