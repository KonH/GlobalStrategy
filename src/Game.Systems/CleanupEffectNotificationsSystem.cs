using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CleanupEffectNotificationsSystem {
		// Called before GameLogic's character-cycling debug-command handlers — RoleChangeApplied
		// is created there, earlier in the tick than CreateActionEffectSystem,
		// so it cannot share UpdateActionEffects' call site without being destroyed the same tick
		// it's created. See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
		public static void UpdateRoleChange(World world) {
			RemoveComponent<RoleChangeApplied>(world);
		}

		// Called before GameLogic's Wars.TryResolvePeaceByChance — WarResolvedApplied is created
		// there (and by the debug StopWar command handler later in the same tick), both earlier
		// than UpdateActionEffects' call site. Sweeping it from UpdateActionEffects would destroy
		// peace/StopWar events before VisualStateConverter runs. Card ResolveWar emits after
		// UpdateActionEffects and is cleaned here on the next tick instead.
		public static void UpdateWarResolved(World world) {
			RemoveComponent<WarResolvedApplied>(world);
		}

		// Called alongside CleanupActionEffectsSystem.Update, before CreateActionEffectSystem
		// creates this tick's batch. Do NOT sweep WarResolvedApplied here —
		// peace/StopWar emit it earlier in the tick (before this call); that component is cleaned
		// only by UpdateWarResolved at the start of the next tick.
		public static void UpdateActionEffects(World world) {
			RemoveComponent<ControlEffectApplied>(world);
			RemoveComponent<OpinionEffectApplied>(world);
			RemoveComponent<RelationSetApplied>(world);
			RemoveComponent<RelationClearedApplied>(world);
			RemoveComponent<WarDeclaredApplied>(world);
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
