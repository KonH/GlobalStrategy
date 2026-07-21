using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class RecruitsGrowthCollectorTests {
		static World CreateWorldWithCountryPopulation(string countryId, double population) {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = "country_population", Value = population });
			return world;
		}

		[Fact]
		void compute_returns_raw_delta_when_under_cap() {
			// population 2000, cap% 15 -> cap 300, increase% 1 -> rawDelta 20; currentValue 100 well under cap
			var world = CreateWorldWithCountryPopulation("A", 2000.0);
			var collector = new RecruitsGrowthCollector(1.0, 15.0);

			double delta = collector.Compute("A", 100.0, world);

			Assert.Equal(20.0, delta, 6);
		}

		[Fact]
		void compute_clamps_to_remaining_cap_room() {
			// population 2000, cap 300, rawDelta 20; currentValue 290 -> only 10 room left
			var world = CreateWorldWithCountryPopulation("A", 2000.0);
			var collector = new RecruitsGrowthCollector(1.0, 15.0);

			double delta = collector.Compute("A", 290.0, world);

			Assert.Equal(10.0, delta, 6);
		}

		[Fact]
		void compute_returns_zero_when_already_at_cap() {
			// population 2000, cap 300; currentValue == cap
			var world = CreateWorldWithCountryPopulation("A", 2000.0);
			var collector = new RecruitsGrowthCollector(1.0, 15.0);

			double delta = collector.Compute("A", 300.0, world);

			Assert.Equal(0.0, delta, 6);
		}

		[Fact]
		void compute_never_returns_negative_delta_when_population_shrinks() {
			// population dropped to 1000 -> cap 150, rawDelta 10; currentValue 200 already above the new cap
			var world = CreateWorldWithCountryPopulation("A", 1000.0);
			var collector = new RecruitsGrowthCollector(1.0, 15.0);

			double delta = collector.Compute("A", 200.0, world);

			Assert.Equal(0.0, delta, 6);
		}
	}
}
