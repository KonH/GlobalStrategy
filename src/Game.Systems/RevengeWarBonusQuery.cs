using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class RevengeWarBonusQuery {
		public static double GetBonusPercent(IReadOnlyWorld world, string countryId, string kind) {
			int[] required = { TypeId<RevengeWarBonus>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				RevengeWarBonus[] bonuses = arch.GetColumn<RevengeWarBonus>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (bonuses[i].CountryId != countryId) {
						continue;
					}
					if (kind == "damage") {
						return bonuses[i].DamageBonusPercent;
					}
					if (kind == "durability") {
						return bonuses[i].DurabilityBonusPercent;
					}
					return 0;
				}
			}
			return 0;
		}

		public static void RemoveForCountry(World world, string countryId) {
			int[] required = { TypeId<RevengeWarBonus>.Value };
			var toDestroy = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				RevengeWarBonus[] bonuses = arch.GetColumn<RevengeWarBonus>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (bonuses[i].CountryId == countryId) {
						toDestroy.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in toDestroy) {
				world.Destroy(e);
			}
		}
	}
}
