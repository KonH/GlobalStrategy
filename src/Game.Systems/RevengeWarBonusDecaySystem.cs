using System;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class RevengeWarBonusDecaySystem {
		public static void Update(
			World world,
			DateTime previousTime,
			DateTime currentTime,
			double damageDecayPerMonth,
			double durabilityDecayPerMonth) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			int[] required = { TypeId<RevengeWarBonus>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				RevengeWarBonus[] bonuses = arch.GetColumn<RevengeWarBonus>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					bonuses[i].DamageBonusPercent = Math.Max(0, bonuses[i].DamageBonusPercent - damageDecayPerMonth);
					bonuses[i].DurabilityBonusPercent = Math.Max(0, bonuses[i].DurabilityBonusPercent - durabilityDecayPerMonth);
				}
			}
		}
	}
}
