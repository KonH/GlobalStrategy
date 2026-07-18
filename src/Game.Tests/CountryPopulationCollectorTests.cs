using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryPopulationCollectorTests {
		static void SeedProvince(World world, string provinceId, string ownerId, double population) {
			ProvinceOwnershipSystem.Seed(world, new ProvinceConfig {
				Provinces = new System.Collections.Generic.List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = provinceId, CountryId = ownerId, Population = population }
				}
			});
			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(provinceId, OwnerType.Province));
			world.Add(resourceEntity, new Resource {
				ResourceId = CountryPopulationCollector.ResourceId,
				Value = population
			});
		}

		[Fact]
		void compute_sums_population_of_owned_provinces() {
			var world = new World();
			SeedProvince(world, "prov_1", "A", 1000.0);
			SeedProvince(world, "prov_2", "A", 2000.0);
			var collector = new CountryPopulationCollector();

			double delta = collector.Compute("A", 0.0, world);

			Assert.Equal(3000.0, delta);
		}

		[Fact]
		void compute_reads_current_runtime_owner_not_seed_country_id() {
			var world = new World();
			SeedProvince(world, "prov_1", "A", 1000.0);
			ProvinceOwnershipSystem.ChangeOwner(world, "prov_1", "B");
			var collector = new CountryPopulationCollector();

			Assert.Equal(0.0, collector.Compute("A", 0.0, world));
			Assert.Equal(1000.0, collector.Compute("B", 0.0, world));
		}

		[Fact]
		void compute_returns_negative_delta_for_zero_owned_provinces() {
			var world = new World();
			var collector = new CountryPopulationCollector();

			double delta = collector.Compute("A", 500.0, world);

			Assert.Equal(-500.0, delta);
		}
	}
}
