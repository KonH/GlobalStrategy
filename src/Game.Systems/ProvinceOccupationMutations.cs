using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ProvinceOccupationMutations {
		public static (bool Changed, string OldOccupierId) Set(
			World world, string provinceId, string occupierId) {
			string normalizedOccupierId = occupierId ?? "";
			int[] required = { TypeId<ProvinceOccupation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOccupation[] occupations = arch.GetColumn<ProvinceOccupation>();
				for (int i = 0; i < arch.Count; i++) {
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

		public static (bool Changed, string OldOccupierId) Clear(World world, string provinceId) {
			return Set(world, provinceId, "");
		}

		public static void BumpVersion(World world) {
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
