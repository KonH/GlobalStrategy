using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ResourceQuery {
		public static double GetValue(IReadOnlyWorld world, string ownerId, string resourceId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return resources[i].Value;
					}
				}
			}
			return 0;
		}
	}
}
