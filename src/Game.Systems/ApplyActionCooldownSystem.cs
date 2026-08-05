using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ApplyActionCooldownSystem {
		public static void Update(World world, DateTime currentTime, GameSettings settings, ActionConfig actionConfig) {
			int[] required = { TypeId<GameAction>.Value, TypeId<ActionSucceeded>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
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
				var def = actionConfig.Find(actionId);
				if (def == null || def.OwnerType != "country") { continue; }

				DateTime endTime = currentTime.AddDays(settings.CardCooldownDays);
				int existing = FindTrackingEntity(world, orgId, actionId);
				if (existing >= 0) {
					world.Get<ActionCooldownState>(existing).EndTime = endTime;
				} else {
					int e = world.Create();
					world.Add(e, new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime });
				}
			}
		}

		static int FindTrackingEntity(IReadOnlyWorld world, string orgId, string actionId) {
			int[] required = { TypeId<ActionCooldownState>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCooldownState[] states = arch.GetColumn<ActionCooldownState>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (states[i].OrgId == orgId && states[i].ActionId == actionId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
