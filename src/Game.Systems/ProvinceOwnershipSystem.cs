using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ProvinceOwnershipSystem {
		public static int Version { get; private set; }

		public static void Seed(World world, ProvinceConfig config) {
			foreach (var entry in config.Provinces) {
				int entity = world.Create();
				world.Add(entity, new ProvinceOwnership {
					ProvinceId = entry.ProvinceId,
					OwnerId = entry.CountryId
				});
			}
			Version++;
		}

		public static (bool Changed, string OldOwnerId) ChangeOwner(World world, string provinceId, string newOwnerId) {
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (ownerships[i].ProvinceId != provinceId) {
						continue;
					}
					string oldOwnerId = ownerships[i].OwnerId;
					if (oldOwnerId == newOwnerId) {
						return (false, "");
					}
					ownerships[i].OwnerId = newOwnerId;
					Version++;
					return (true, oldOwnerId);
				}
			}
			return (false, "");
		}

		public static string GetOwner(IReadOnlyWorld world, string provinceId) {
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (ownerships[i].ProvinceId == provinceId) {
						return ownerships[i].OwnerId;
					}
				}
			}
			return "";
		}

		public static Dictionary<string, List<string>> GetProvincesByOwner(IReadOnlyWorld world) {
			var result = new Dictionary<string, List<string>>();
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!result.TryGetValue(ownerships[i].OwnerId, out var list)) {
						list = new List<string>();
						result[ownerships[i].OwnerId] = list;
					}
					list.Add(ownerships[i].ProvinceId);
				}
			}
			return result;
		}
	}
}
