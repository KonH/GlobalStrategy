using System;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

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

		public static bool TryApplyClampedDelta(
			World world, string ownerId, string resourceId, double delta,
			ResourceDefinition? definition, string effectId, DateTime timestamp,
			double defaultMinimum, double defaultMaximum, out double appliedDelta) {
			double minimum = definition?.MinValue ?? defaultMinimum;
			double maximum = definition?.MaxValue ?? defaultMaximum;
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				appliedDelta = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			double oldValue = resource.Value;
			resource.Value = Math.Clamp(oldValue + delta, minimum, maximum);
			appliedDelta = resource.Value - oldValue;
			if (appliedDelta != 0) {
				TryAppendHistory(world, entity, definition, effectId, appliedDelta, timestamp);
			}
			return true;
		}

		public static void TryAppendHistory(
			World world, int resourceEntity, ResourceDefinition? definition,
			string effectId, double appliedDelta, DateTime timestamp) {
			if (appliedDelta == 0 || definition?.RecordHistory != true) {
				return;
			}
			if (!world.Has<ResourceHistory>(resourceEntity)) {
				world.Add(resourceEntity, new ResourceHistory {
					History = new System.Collections.Generic.List<ResourceChangeEntry>()
				});
			}
			ref ResourceHistory history = ref world.Get<ResourceHistory>(resourceEntity);
			if (history.History == null) {
				history.History = new System.Collections.Generic.List<ResourceChangeEntry>();
			}
			history.History.Add(new ResourceChangeEntry {
				EffectId = effectId,
				AppliedDelta = appliedDelta,
				Timestamp = timestamp
			});
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
