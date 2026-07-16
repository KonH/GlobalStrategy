using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ActionPlayability {
		public static bool Evaluate(IReadOnlyWorld world, ActionConfig config, string actionId, string orgId, string? countryId) {
			var def = config.Find(actionId);
			if (def == null) { return false; }

			int orgControl = GetOrgControl(world, orgId, countryId);
			var ctx = new ExpressionContext { Control = orgControl };

			foreach (var cond in def.Conditions) {
				if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { return false; }
			}

			if (!CanAfford(world, orgId, def.Cost)) { return false; }

			return true;
		}

		public static int GetOrgControl(IReadOnlyWorld world, string orgId, string? countryId) {
			if (string.IsNullOrEmpty(countryId)) { return 0; }
			int total = 0;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == orgId && effects[i].CountryId == countryId) {
						total += effects[i].Value;
					}
				}
			}
			return total;
		}

		public static bool CanAfford(IReadOnlyWorld world, string orgId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				int entity = FindResourceEntity(world, orgId, cost.ResourceId);
				if (entity < 0 || world.Get<Resource>(entity).Value < cost.Amount) { return false; }
			}
			return true;
		}

		public static int FindResourceEntity(IReadOnlyWorld world, string ownerId, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
