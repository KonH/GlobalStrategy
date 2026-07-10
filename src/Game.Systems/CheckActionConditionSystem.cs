using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CheckActionConditionSystem {
		public static void Update(World world, ActionConfig config) {
			int[] required = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value };
			var toValidate = new List<(int entity, string actionId, string orgId)>();

			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					toValidate.Add((arch.Entities[i], actions[i].ActionId, orgs[i].OrgId));
				}
			}

			int[] countryRequired = { TypeId<CountryContext>.Value };
			var entityCountry = new Dictionary<int, string>();
			foreach (var arch in world.GetMatchingArchetypes(countryRequired, null)) {
				CountryContext[] ctxs = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					entityCountry[arch.Entities[i]] = ctxs[i].CountryId;
				}
			}

			var toAdd = new List<int>();
			foreach (var (entity, actionId, orgId) in toValidate) {
				entityCountry.TryGetValue(entity, out string countryId);
				var def = config.Find(actionId);
				if (def == null) { continue; }

				int orgControl = GetOrgControl(world, orgId, countryId);
				var ctx = new ExpressionContext { Control = orgControl };

				bool conditionsMet = true;
				foreach (var cond in def.Conditions) {
					if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { conditionsMet = false; break; }
				}
				if (!conditionsMet) { continue; }

				if (!CanAfford(world, orgId, def.Cost)) { continue; }

				toAdd.Add(entity);
			}

			foreach (int e in toAdd) {
				world.Add(e, new ActionValid());
			}
		}

		static int GetOrgControl(World world, string orgId, string countryId) {
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

		static bool CanAfford(World world, string orgId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				if (!HasResource(world, orgId, cost.ResourceId, cost.Amount)) { return false; }
			}
			return true;
		}

		static bool HasResource(World world, string ownerId, string resourceId, double amount) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return resources[i].Value >= amount;
					}
				}
			}
			return false;
		}
	}
}
