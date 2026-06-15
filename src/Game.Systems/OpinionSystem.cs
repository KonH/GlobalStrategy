using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class OpinionSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			int[] required = { TypeId<CharacterOpinion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CharacterOpinion[] opinions = arch.GetColumn<CharacterOpinion>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ref CharacterOpinion opinion = ref opinions[i];
					if (opinion.ModifiersPerOrg == null) {
						continue;
					}
					foreach (var orgKey in opinion.ModifiersPerOrg.Keys) {
						var list = opinion.ModifiersPerOrg[orgKey];
						for (int j = list.Count - 1; j >= 0; j--) {
							var mod = list[j];
							int prev = mod.Value;
							mod.Value += mod.ChangeValue;
							if (prev > 0 && mod.Value < 0) { mod.Value = 0; }
							else if (prev < 0 && mod.Value > 0) { mod.Value = 0; }
							list[j] = mod;
							if (mod.Value == 0) {
								list.RemoveAt(j);
							}
						}
					}
				}
			}
		}
	}
}
