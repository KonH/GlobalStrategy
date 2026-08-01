using ECS;
using GS.Game.Components;
using System.Collections.Generic;

namespace GS.Game.Systems {
	public static class RevengeEligibilityQuery {
		public static List<string> GetTargetCountryIds(IReadOnlyWorld world, string countryId) {
			var result = new List<string>();
			int[] required = { TypeId<RevengeEligibility>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				RevengeEligibility[] items = arch.GetColumn<RevengeEligibility>();
				for (int i = 0; i < arch.Count; i++) {
					if (items[i].CountryId == countryId) { result.Add(items[i].TargetCountryId); }
				}
			}
			return result;
		}
		public static bool IsEligible(IReadOnlyWorld world, string countryId, string targetCountryId) {
			return FindEntity(world, countryId, targetCountryId) >= 0;
		}

		public static void SetEligible(World world, string countryId, string targetCountryId) {
			if (FindEntity(world, countryId, targetCountryId) >= 0) {
				return;
			}
			int entity = world.Create();
			world.Add(entity, new RevengeEligibility { CountryId = countryId, TargetCountryId = targetCountryId });
		}

		public static void ClearEligible(World world, string countryId, string targetCountryId) {
			int entity = FindEntity(world, countryId, targetCountryId);
			if (entity >= 0) {
				world.Destroy(entity);
			}
		}

		// Called whenever a war between two countries resolves: the loser becomes eligible for
		// revenge against the winner, and the winner's own eligibility against the loser (if any,
		// from a prior loss) is cleared since it has now been avenged.
		public static void OnWarResolved(World world, string winnerCountryId, string loserCountryId) {
			SetEligible(world, loserCountryId, winnerCountryId);
			ClearEligible(world, winnerCountryId, loserCountryId);
		}

		static int FindEntity(IReadOnlyWorld world, string countryId, string targetCountryId) {
			int[] required = { TypeId<RevengeEligibility>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				RevengeEligibility[] items = arch.GetColumn<RevengeEligibility>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (items[i].CountryId == countryId && items[i].TargetCountryId == targetCountryId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
