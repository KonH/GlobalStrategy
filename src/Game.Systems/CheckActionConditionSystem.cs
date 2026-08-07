using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CheckActionConditionSystem {
		public static void Update(
			World world,
			ActionConfig config,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			DateTime currentTime = default,
			int maxControlPool = 100) {
			int[] required = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
			var toValidate = new List<(int entity, string actionId, string orgId, string countryId)>();

			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardUse[] uses = arch.GetColumn<CardUse>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					toValidate.Add((arch.Entities[i], actions[i].ActionId, orgs[i].OrgId, uses[i].CountryId));
				}
			}

			var toAdd = new List<int>();
			foreach (var (entity, actionId, orgId, countryId) in toValidate) {
				if (ActionPlayability.Evaluate(
					world, config, entity, actionId, orgId, countryId,
					hqCountryByOrgId, currentTime, maxControlPool).CanPlay) {
					toAdd.Add(entity);
				}
			}

			foreach (int e in toAdd) {
				world.Add(e, new ActionValid());
			}
		}
	}
}
