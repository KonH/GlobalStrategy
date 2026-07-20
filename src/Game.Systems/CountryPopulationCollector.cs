using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class CountryPopulationCollector : IResourceCollector {
		public const string Id = "country_population_aggregate";

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			var provincesByOwner = ProvinceOwnershipSystem.GetProvincesByOwner(world);

			var populationByProvinceId = new Dictionary<string, double>();
			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerType != OwnerType.Province || resources[i].ResourceId != ResourceDefinitions.Population) {
						continue;
					}
					populationByProvinceId[owners[i].OwnerId] = resources[i].Value;
				}
			}

			double freshTotal = 0;
			if (provincesByOwner.TryGetValue(ownerId, out var provinceIds)) {
				foreach (var provinceId in provinceIds) {
					if (populationByProvinceId.TryGetValue(provinceId, out double population)) {
						freshTotal += population;
					}
				}
			}

			return freshTotal - currentValue;
		}
	}
}
