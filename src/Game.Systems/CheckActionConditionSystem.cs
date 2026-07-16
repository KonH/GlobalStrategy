using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CheckActionConditionSystem {
		public static void Update(World world, ActionConfig config) {
			int[] required = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
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
				if (ActionPlayability.Evaluate(world, config, actionId, orgId, countryId)) {
					toAdd.Add(entity);
				}
			}

			foreach (int e in toAdd) {
				world.Add(e, new ActionValid());
			}
		}
	}
}
