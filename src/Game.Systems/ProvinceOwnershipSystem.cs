using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ProvinceOwnershipSystem {
		public static void Seed(World world, ProvinceConfig config) {
			foreach (var entry in config.Provinces) {
				int entity = world.Create();
				world.Add(entity, new ProvinceOwnership {
					ProvinceId = entry.ProvinceId,
					OwnerId = entry.CountryId
				});
			}
			BumpVersion(world);
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
					BumpVersion(world);
					return (true, oldOwnerId);
				}
			}
			return (false, "");
		}

		// Per-World change counter for VisualStateConverter's dirty-check — see
		// ecs_patterns.md: no implicit singletons, state must be scoped per-World.
		public static int GetVersion(IReadOnlyWorld world) {
			int[] required = { TypeId<ProvinceOwnershipVersion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<ProvinceOwnershipVersion>()[0].Value;
				}
			}
			return 0;
		}

		static void BumpVersion(World world) {
			int[] required = { TypeId<ProvinceOwnershipVersion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					arch.GetColumn<ProvinceOwnershipVersion>()[0].Value++;
					return;
				}
			}
			int entity = world.Create();
			world.Add(entity, new ProvinceOwnershipVersion { Value = 1 });
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
