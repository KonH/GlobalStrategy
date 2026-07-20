using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class RecruitsSeedCollector : IResourceCollector {
		public const string Id = "recruits_seed";

		readonly double _initialPercent;

		public RecruitsSeedCollector(double initialPercent) {
			_initialPercent = initialPercent;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			double population = ResourceQuery.GetValue(world, ownerId, ResourceDefinitions.CountryPopulation);
			return population * _initialPercent / 100.0 - currentValue;
		}
	}
}
