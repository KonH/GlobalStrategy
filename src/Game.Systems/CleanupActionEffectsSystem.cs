using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CleanupActionEffectsSystem {
		public static void Update(World world) {
			// GameAction is persistent card identity (Savable) — not cleaned here.
			RemoveComponent<ActionValid>(world);
			RemoveComponent<ActionSucceeded>(world);
			RemoveComponent<ActionFailed>(world);
			RemoveComponent<CardUse>(world);
			RemoveComponent<DiscoverCountryEffect>(world);
			RemoveComponent<ResourceChange>(world);
		}

		static void RemoveComponent<T>(World world) where T : struct {
			int[] required = { TypeId<T>.Value };
			var entities = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					entities.Add(arch.Entities[i]);
				}
			}
			foreach (int e in entities) {
				world.Remove<T>(e);
			}
		}
	}
}
