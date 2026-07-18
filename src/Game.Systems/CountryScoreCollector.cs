using ECS;

namespace GS.Game.Systems {
	public sealed class CountryScoreCollector : IResourceCollector {
		public const string Id = "country_score_formula";
		public const string ResourceId = "country_score";
		public const string CountryPopulationResourceId = "country_population";

		readonly double _coefficient;

		public CountryScoreCollector(double coefficient) {
			_coefficient = coefficient;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			double population = ResourceQuery.GetValue(world, ownerId, CountryPopulationResourceId);
			return population * _coefficient - currentValue;
		}
	}
}
