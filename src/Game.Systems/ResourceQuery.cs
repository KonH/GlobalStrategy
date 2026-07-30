using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ResourceQuery {
		public static double GetValue(IReadOnlyWorld world, string ownerId, string resourceId) {
			return TryGetValue(world, ownerId, resourceId, out double value) ? value : 0;
		}

		public static bool TryGetValue(IReadOnlyWorld world, string ownerId, string resourceId, out double value) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						value = resources[i].Value;
						return true;
					}
				}
			}
			value = 0;
			return false;
		}
	}
}
