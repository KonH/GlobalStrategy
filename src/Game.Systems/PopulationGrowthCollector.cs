using ECS;

namespace GS.Game.Systems {
	public sealed class PopulationGrowthCollector : IResourceCollector {
		public const string Id = "population_growth";

		readonly double _percentPerMonth;

		public PopulationGrowthCollector(double percentPerMonth) {
			_percentPerMonth = percentPerMonth;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			return currentValue * _percentPerMonth / 100.0;
		}
	}
}
