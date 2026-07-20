using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryScoreCollectorTests {
		[Fact]
		void compute_returns_coefficient_times_country_population() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("A", OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = "country_population", Value = 2000.0 });
			var collector = new CountryScoreCollector(0.01);

			double delta = collector.Compute("A", 0.0, world);

			Assert.Equal(20.0, delta, 6);
		}
	}
}
