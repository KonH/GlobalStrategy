using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class InitActionFromPlayCardSystem {
		public static void Update(World world, ReadCommands<PlayCardActionCommand> commands) {
			foreach (var cmd in commands.AsSpan()) {
				if (string.IsNullOrEmpty(cmd.OrgId)) { continue; }
				if (string.IsNullOrEmpty(cmd.CountryId)) {
					InitOrgCard(world, cmd.OrgId, cmd.ActionId);
				} else {
					InitCountryCard(world, cmd.OrgId, cmd.CountryId, cmd.ActionId);
				}
			}
		}

		static void InitOrgCard(World world, string orgId, string actionId) {
			int[] required = { TypeId<ActionCard>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId != orgId || cards[i].ActionId != actionId) { continue; }
					int entity = arch.Entities[i];
					if (world.Has<GameAction>(entity)) {
						throw new InvalidOperationException($"Duplicate PlayCardActionCommand for org={orgId} action={actionId}");
					}
					world.Add(entity, new GameAction { ActionId = actionId });
					world.Add(entity, new OrgContext { OrgId = orgId });
					world.Add(entity, new CardUse());
					return;
				}
			}
		}

		static void InitCountryCard(World world, string orgId, string countryId, string actionId) {
			int[] required = { TypeId<ActionCard>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<InHand>.Value };
			var candidates = new System.Collections.Generic.List<int>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId || countries[i].CountryId != countryId || cards[i].ActionId != actionId) { continue; }
					candidates.Add(arch.Entities[i]);
				}
			}
			foreach (int entity in candidates) {
				if (world.Has<GameAction>(entity)) {
					throw new InvalidOperationException($"Duplicate PlayCardActionCommand for org={orgId} country={countryId} action={actionId}");
				}
				world.Add(entity, new GameAction { ActionId = actionId });
				world.Add(entity, new CardUse());
				return;
			}
		}
	}
}
