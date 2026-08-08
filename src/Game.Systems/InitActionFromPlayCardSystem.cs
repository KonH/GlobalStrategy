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
					InitCard(world, cmd.OrgId, CardOwnerKind.Org, "", cmd.ActionId, cmd.TargetCountryId, cmd.SlotIndex);
				} else {
					InitCard(world, cmd.OrgId, CardOwnerKind.Country, cmd.CountryId, cmd.ActionId, cmd.TargetCountryId, cmd.SlotIndex);
				}
			}
		}

		static void InitCard(
			World world,
			string orgId,
			CardOwnerKind ownerKind,
			string selectedCountryId,
			string actionId,
			string targetCountryId,
			int slotIndex) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId
						|| owners[i].Value != ownerKind
						|| actions[i].ActionId != actionId) {
						continue;
					}
					int entity = arch.Entities[i];
					string cardTargetCountryId = world.Has<RelationCardTarget>(entity)
						? world.Get<RelationCardTarget>(entity).TargetCountryId
						: world.Has<RevengeCardTarget>(entity) ? world.Get<RevengeCardTarget>(entity).TargetCountryId : "";
					if (cardTargetCountryId != (targetCountryId ?? "")) { continue; }
					if (hands[i].SlotIndex != slotIndex) { continue; }
					Activate(world, entity, orgId, ownerKind, selectedCountryId, actionId, slotIndex);
					return;
				}
			}
		}

		static void Activate(
			World world,
			int entity,
			string orgId,
			CardOwnerKind ownerKind,
			string selectedCountryId,
			string actionId,
			int slotIndex) {
			if (world.Has<CardUse>(entity)) {
				throw new InvalidOperationException(
					$"Duplicate PlayCardActionCommand for org={orgId} owner={ownerKind} slot={slotIndex} action={actionId}");
			}
			world.Add(entity, new CardUse { CountryId = selectedCountryId });
		}
	}
}
