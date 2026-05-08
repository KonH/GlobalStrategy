using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ResourceSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;

			int[] effectRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};

			var toApply = new List<(string OwnerId, string ResourceId, double Value)>();
			var toDestroy = new List<int>();

			foreach (Archetype arch in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++) {
					var effect = effects[i];
					bool shouldApply = effect.PayType == PayType.Instant
						|| (effect.PayType == PayType.Monthly && isMonthBoundary);
					if (!shouldApply) {
						continue;
					}
					toApply.Add((owners[i].OwnerId, links[i].ResourceId, effect.Value));
					if (effect.PayType == PayType.Instant) {
						toDestroy.Add(entities[i]);
					}
				}
			}

			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};

			foreach ((string ownerId, string resourceId, double value) in toApply) {
				foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
							resources[i].Value += value;
						}
					}
				}
			}

			foreach (int e in toDestroy) {
				world.Destroy(e);
			}
		}
	}
}
