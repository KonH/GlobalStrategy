using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class OrgMetrics {
		public static int GetTotalControl(IReadOnlyWorld world, string orgId) {
			int total = 0;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == orgId) { total += effects[i].Value; }
				}
			}
			return total;
		}

		public static double GetGold(IReadOnlyWorld world, string orgId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == orgId && resources[i].ResourceId == ResourceDefinitions.Gold) {
						return resources[i].Value;
					}
				}
			}
			return 0.0;
		}

		public static Dictionary<string, int> GetControlByCountry(IReadOnlyWorld world, string orgId) {
			return GetControlByCountry(world, orgId, null);
		}

		public static Dictionary<string, int> GetControlByCountry(
			IReadOnlyWorld world,
			string orgId,
			IReadOnlyCollection<string>? availableCountryIds) {
			var result = new Dictionary<string, int>();
			HashSet<string>? availableCountries = availableCountryIds == null
				? null
				: new HashSet<string>(availableCountryIds, System.StringComparer.Ordinal);
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId != orgId
						|| (availableCountries != null && !availableCountries.Contains(effects[i].CountryId))) {
						continue;
					}
					result.TryGetValue(effects[i].CountryId, out int existing);
					result[effects[i].CountryId] = existing + effects[i].Value;
				}
			}
			return result;
		}
	}
}
