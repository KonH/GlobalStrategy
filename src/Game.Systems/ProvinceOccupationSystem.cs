using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ProvinceOccupationSystem {
		public static void Seed(World world, ProvinceConfig config) {
			foreach (var entry in config.Provinces) {
				int entity = world.Create();
				world.Add(entity, new ProvinceOccupation {
					ProvinceId = entry.ProvinceId,
					OccupierId = ""
				});
			}
			BumpVersion(world);
		}

		public static (bool Changed, string OldOccupierId) SetOccupier(World world, string provinceId, string occupierId) {
			string normalizedOccupierId = occupierId ?? "";
			int[] required = { TypeId<ProvinceOccupation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOccupation[] occupations = arch.GetColumn<ProvinceOccupation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (occupations[i].ProvinceId != provinceId) {
						continue;
					}
					string oldOccupierId = occupations[i].OccupierId ?? "";
					if (oldOccupierId == normalizedOccupierId) {
						return (false, "");
					}
					occupations[i].OccupierId = normalizedOccupierId;
					BumpVersion(world);
					return (true, oldOccupierId);
				}
			}
			return (false, "");
		}

		public static (bool Changed, string OldOccupierId) ClearOccupier(World world, string provinceId) {
			return SetOccupier(world, provinceId, "");
		}

		public static (bool Changed, string OldOccupierId, string NewOccupierId) ToggleOccupier(World world, string provinceId, string occupierId) {
			string normalizedOccupierId = occupierId ?? "";
			string currentOccupierId = GetOccupier(world, provinceId);
			string newOccupierId = currentOccupierId == normalizedOccupierId ? "" : normalizedOccupierId;
			var (changed, oldOccupierId) = SetOccupier(world, provinceId, newOccupierId);
			return (changed, oldOccupierId, changed ? newOccupierId : "");
		}

		public static string GetOccupier(IReadOnlyWorld world, string provinceId) {
			int[] required = { TypeId<ProvinceOccupation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOccupation[] occupations = arch.GetColumn<ProvinceOccupation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (occupations[i].ProvinceId == provinceId) {
						return occupations[i].OccupierId ?? "";
					}
				}
			}
			return "";
		}

		public static Dictionary<string, string> GetOccupierByProvinceId(IReadOnlyWorld world) {
			var result = new Dictionary<string, string>();
			int[] required = { TypeId<ProvinceOccupation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOccupation[] occupations = arch.GetColumn<ProvinceOccupation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string occupierId = occupations[i].OccupierId ?? "";
					if (occupierId == "") {
						continue;
					}
					result[occupations[i].ProvinceId] = occupierId;
				}
			}
			return result;
		}

		public static int GetVersion(IReadOnlyWorld world) {
			int[] required = { TypeId<ProvinceOccupationVersion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<ProvinceOccupationVersion>()[0].Value;
				}
			}
			return 0;
		}

		static void BumpVersion(World world) {
			int[] required = { TypeId<ProvinceOccupationVersion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					arch.GetColumn<ProvinceOccupationVersion>()[0].Value++;
					return;
				}
			}
			int entity = world.Create();
			world.Add(entity, new ProvinceOccupationVersion { Value = 1 });
		}
	}
}
