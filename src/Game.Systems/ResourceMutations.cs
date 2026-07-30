using System;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ResourceMutations {
		public static bool TrySetValue(
			World world, string ownerId, string resourceId, double value, out double oldValue) {
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				oldValue = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			oldValue = resource.Value;
			resource.Value = value;
			return true;
		}

		public static bool TryApplyClampedDelta(
			World world, string ownerId, string resourceId, double delta,
			double minimum, double maximum, out double appliedDelta) {
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				appliedDelta = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			double oldValue = resource.Value;
			resource.Value = Math.Clamp(oldValue + delta, minimum, maximum);
			appliedDelta = resource.Value - oldValue;
			return true;
		}

		static int FindEntity(IReadOnlyWorld world, string ownerId, string resourceId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
