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

		static World CreateWorldWithPopulation(string provinceId, double initialValue, out int resourceEntity) {
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
			var world = CreateWorldWithPopulation("Russia__moscow", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan1, Jan2, 0.075);
			Assert.Equal(1000.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void population_grows_by_percent_at_month_boundary() {
			var world = CreateWorldWithPopulation("Russia__moscow", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			Assert.Equal(1000.75, world.Get<Resource>(re).Value, 6);
		}

		[Fact]
		void growth_compounds_across_multiple_months() {
			var world = CreateWorldWithPopulation("Russia__moscow", 1000.0, out int re);
			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);
			double afterFirst = world.Get<Resource>(re).Value;

			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ProvincePopulationGrowthSystem.Update(world, feb28, mar1, 0.075);
			double afterSecond = world.Get<Resource>(re).Value;

			Assert.Equal(1000.75, afterFirst, 6);
			Assert.Equal(afterFirst * 1.00075, afterSecond, 6);
			Assert.NotEqual(afterFirst, afterSecond);
		}

		[Fact]
		void only_province_owner_type_and_matching_resource_id_affected() {
			var world = new World();

			int countryOwnedPopulation = world.Create();
			world.Add(countryOwnedPopulation, new ResourceOwner("Russia", OwnerType.Country));
			world.Add(countryOwnedPopulation, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = 1000.0
			});

			int provinceOwnedGold = world.Create();
			world.Add(provinceOwnedGold, new ResourceOwner("Russia__moscow", OwnerType.Province));
			world.Add(provinceOwnedGold, new Resource { ResourceId = "gold", Value = 100.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			Assert.Equal(1000.0, world.Get<Resource>(countryOwnedPopulation).Value);
			Assert.Equal(100.0, world.Get<Resource>(provinceOwnedGold).Value);
		}

		[Fact]
		void two_provinces_of_same_owner_diverge_independently() {
			var world = new World();

			int re1 = world.Create();
			world.Add(re1, new ResourceOwner("Russia__moscow", OwnerType.Province));
			world.Add(re1, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 1000.0 });

			int re2 = world.Create();
			world.Add(re2, new ResourceOwner("Russia__kiev", OwnerType.Province));
			world.Add(re2, new Resource { ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId, Value = 2000.0 });

			ProvincePopulationGrowthSystem.Update(world, Jan31, Feb1, 0.075);

			double v1 = world.Get<Resource>(re1).Value;
			double v2 = world.Get<Resource>(re2).Value;

			Assert.NotEqual(v1, v2);
			Assert.Equal(1000.0 * 1.00075, v1, 6);
			Assert.Equal(2000.0 * 1.00075, v2, 6);
		}
	}
}
