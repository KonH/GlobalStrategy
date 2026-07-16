using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class DeductActionCostSystem {
		public static void Update(World world, ActionConfig config) {
			int[] required = { TypeId<GameAction>.Value, TypeId<ActionValid>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
			var toProcess = new List<(string actionId, string orgId)>();

			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					toProcess.Add((actions[i].ActionId, orgs[i].OrgId));
				}
			}

			foreach (var (actionId, orgId) in toProcess) {
				var def = config.Find(actionId);
				if (def == null) { continue; }

				foreach (var cost in def.Cost) {
					DeductResource(world, orgId, cost.ResourceId, cost.Amount);
					int changeEntity = world.Create();
					world.Add(changeEntity, new ResourceChange {
						EffectId = $"cost_{orgId}_{actionId}_{cost.ResourceId}",
						ResourceId = cost.ResourceId,
						OwnerId = orgId,
						Amount = -cost.Amount
					});
				}
			}
		}

		static void DeductResource(World world, string ownerId, string resourceId, double amount) {
			int entity = ActionPlayability.FindResourceEntity(world, ownerId, resourceId);
			if (entity >= 0) {
				ref Resource r = ref world.Get<Resource>(entity);
				r.Value -= amount;
			}
		}
	}
}
