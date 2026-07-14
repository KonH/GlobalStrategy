using System;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ProvincePopulationGrowthSystem {
		public const string PopulationResourceId = "population";

		public static void Update(World world, DateTime previousTime, DateTime currentTime, double monthlyGrowthPercent) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};

			double multiplier = 1.0 + monthlyGrowthPercent / 100.0;

			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerType != OwnerType.Province || resources[i].ResourceId != PopulationResourceId) {
						continue;
					}
					resources[i].Value *= multiplier;
				}
			}
		}
	}
}
