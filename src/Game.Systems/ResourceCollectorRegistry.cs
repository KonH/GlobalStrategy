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

		public static ResourceCollectorRegistry CreateDefault(
			double populationGrowthPercentPerMonth, double countryScoreCoefficient,
			double recruitsInitialPercent, double recruitsCapPercent, double recruitsMonthlyIncreasePercent) {
			var registry = new ResourceCollectorRegistry();
			registry.Register(PopulationGrowthCollector.Id, new PopulationGrowthCollector(populationGrowthPercentPerMonth));
			registry.Register(CountryPopulationCollector.Id, new CountryPopulationCollector());
			registry.Register(CountryScoreCollector.Id, new CountryScoreCollector(countryScoreCoefficient));
			registry.Register(RecruitsSeedCollector.Id, new RecruitsSeedCollector(recruitsInitialPercent));
			registry.Register(RecruitsGrowthCollector.Id, new RecruitsGrowthCollector(recruitsMonthlyIncreasePercent, recruitsCapPercent));
			registry.Register(OrgScoreCollector.Id, new OrgScoreCollector());
			return registry;
		}
	}
}
