using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CreateActionEffectSystem {
		public static void Update(
			World world,
			ActionConfig actionConfig,
			EffectConfig effectConfig,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			ResourceQuery resources,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			CountryConfig? countryConfig = null) {
			int[] required = { TypeId<GameAction>.Value, TypeId<ActionSucceeded>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
			var toProcess = new List<(int entity, string actionId, string orgId, string countryId)>();

			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardUse[] uses = arch.GetColumn<CardUse>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					toProcess.Add((arch.Entities[i], actions[i].ActionId, orgs[i].OrgId, uses[i].CountryId));
				}
			}

			foreach (var (entity, actionId, orgId, countryId) in toProcess) {
				var def = actionConfig.Find(actionId);
				if (def == null) { continue; }

				EffectApplicator.ApplyEffectIds(
					world,
					effectConfig,
					def.EffectIds,
					orgId,
					countryId,
					currentTime,
					rng,
					settings,
					topology,
					provinceCenters,
					maxControlPool,
					resources,
					countryConfig,
					contextEntity: entity,
					correlationId: actionId,
					targetRole: def.TargetRole);
			}
		}
	}
}
