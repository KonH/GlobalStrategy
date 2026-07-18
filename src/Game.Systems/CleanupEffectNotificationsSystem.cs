using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CleanupEffectNotificationsSystem {
		// Called before GameLogic's character-cycling debug-command handlers — RoleChangeApplied
		// is created there, earlier in the tick than CreateActionEffectSystem/DiscoverCountrySystem,
		// so it cannot share UpdateActionEffects' call site without being destroyed the same tick
		// it's created. See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
		public static void UpdateRoleChange(World world) {
			RemoveComponent<RoleChangeApplied>(world);
		}

		// Called alongside CleanupActionEffectsSystem.Update, before CreateActionEffectSystem/
		// DiscoverCountrySystem create this tick's batch. See ordering note above.
		public static void UpdateActionEffects(World world) {
			RemoveComponent<ControlEffectApplied>(world);
			RemoveComponent<OpinionEffectApplied>(world);
			RemoveComponent<DiscoveryApplied>(world);
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
