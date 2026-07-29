using System;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class WarSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime, double decayPerMonth) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			int[] required = { TypeId<WarProgress>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				WarProgress[] progresses = arch.GetColumn<WarProgress>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					progresses[i].Value = Math.Max(-100, progresses[i].Value - decayPerMonth);
				}
			}
		}
	}
}
