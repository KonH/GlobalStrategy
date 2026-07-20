using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class CountryScoreCollector : IResourceCollector {
		public const string Id = "country_score_formula";

		readonly double _coefficient;

		public CountryScoreCollector(double coefficient) {
			_coefficient = coefficient;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			double population = ResourceQuery.GetValue(world, ownerId, ResourceDefinitions.CountryPopulation);
			return population * _coefficient - currentValue;
		}
	}
}
