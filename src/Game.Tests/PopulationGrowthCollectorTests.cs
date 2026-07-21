using ECS;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class PopulationGrowthCollectorTests {
		[Fact]
		void compute_returns_percent_of_current_value() {
			var world = new World();
			var collector = new PopulationGrowthCollector(0.075);

			double delta = collector.Compute("prov_a", 1000.0, world);

			Assert.Equal(0.75, delta, 6);
		}
	}
}
