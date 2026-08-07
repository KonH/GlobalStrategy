using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class WarSystem {
		public static void Update(
			World world, DateTime previousTime, DateTime currentTime,
			double decayPerMonth, ResourceQuery resources, ResourceConfig? resourceConfig = null) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			ResourceDefinition? definition = resourceConfig?.FindResource(ResourceDefinitions.WarProgress);
			int[] required = { TypeId<War>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				War[] wars = arch.GetColumn<War>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ResourceMutations.TryApplyClampedDelta(
						resources, world, wars[i].WarId, ResourceDefinitions.WarProgress, -decayPerMonth,
						definition, "war_progress_decay", currentTime,
						-100, 100, out _);
				}
			}
		}
	}
}
