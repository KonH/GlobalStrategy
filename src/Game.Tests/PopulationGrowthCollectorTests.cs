using ECS;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class PopulationGrowthCollectorTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		[Fact]
		void compute_returns_percent_of_current_value() {
			var world = new World();
			var collector = new PopulationGrowthCollector(0.075);

			double delta = collector.Compute("prov_a", 1000.0, world, _resources);

			Assert.Equal(0.75, delta, 6);
		}
	}
}
