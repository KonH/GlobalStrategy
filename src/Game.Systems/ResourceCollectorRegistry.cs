using System;
using System.Collections.Generic;

namespace GS.Game.Systems {
	public sealed class ResourceCollectorRegistry {
		readonly Dictionary<string, IResourceCollector> _collectors = new();

		public void Register(string collectorId, IResourceCollector collector) => _collectors[collectorId] = collector;

		public IResourceCollector Resolve(string collectorId) {
			if (!_collectors.TryGetValue(collectorId, out var collector)) {
				throw new InvalidOperationException($"Unknown resource collector id: {collectorId}");
			}
			return collector;
		}

		public static ResourceCollectorRegistry CreateDefault(double populationGrowthPercentPerMonth, double countryScoreCoefficient) {
			var registry = new ResourceCollectorRegistry();
			registry.Register(PopulationGrowthCollector.Id, new PopulationGrowthCollector(populationGrowthPercentPerMonth));
			registry.Register(CountryPopulationCollector.Id, new CountryPopulationCollector());
			registry.Register(CountryScoreCollector.Id, new CountryScoreCollector(countryScoreCoefficient));
			return registry;
		}
	}
}
