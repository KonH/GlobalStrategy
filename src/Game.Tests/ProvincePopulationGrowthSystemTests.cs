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
		}

		[Fact]
		void only_province_owner_type_and_matching_resource_id_affected() {
			var world = new World();

			int countryOwned = world.Create();
			world.Add(countryOwned, new ResourceOwner("Russia", OwnerType.Country));
			world.Add(countryOwned, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 500.0
			});

			int provinceOwnedOtherResource = world.Create();
			world.Add(provinceOwnedOtherResource, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provinceOwnedOtherResource, new Resource { ResourceId = "gold", Value = 500.0 });

			int provinceOwnedPopulation = world.Create();
			world.Add(provinceOwnedPopulation, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provinceOwnedPopulation, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 500.0
			});

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(500.0, world.Get<Resource>(countryOwned).Value);
			Assert.Equal(500.0, world.Get<Resource>(provinceOwnedOtherResource).Value);
			Assert.Equal(500.375, world.Get<Resource>(provinceOwnedPopulation).Value, 6);
		}

		[Fact]
		void two_provinces_of_same_owner_diverge_independently() {
			var world = new World();

			int provA = world.Create();
			world.Add(provA, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provA, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 1000.0
			});

			int provB = world.Create();
			world.Add(provB, new ResourceOwner("prov_b", OwnerType.Province));
			world.Add(provB, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 2000.0
			});

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			double valueA = world.Get<Resource>(provA).Value;
			double valueB = world.Get<Resource>(provB).Value;

			Assert.Equal(1000.75, valueA, 6);
			Assert.Equal(2001.5, valueB, 6);
			Assert.Equal(valueA / 1000.0, valueB / 2000.0, 9);
		}
	}
}
