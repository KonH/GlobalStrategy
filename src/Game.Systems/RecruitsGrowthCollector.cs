using System;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class RecruitsGrowthCollector : IResourceCollector {
		public const string Id = "recruits_growth";

		readonly double _increasePercent;
		readonly double _capPercent;

		public RecruitsGrowthCollector(double increasePercent, double capPercent) {
			_increasePercent = increasePercent;
			_capPercent = capPercent;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			double population = ResourceQuery.GetValue(world, ownerId, ResourceDefinitions.CountryPopulation);
			double cap = population * _capPercent / 100.0;
			double rawDelta = population * _increasePercent / 100.0;
			return Math.Max(0.0, Math.Min(rawDelta, cap - currentValue));
		}
	}
}
