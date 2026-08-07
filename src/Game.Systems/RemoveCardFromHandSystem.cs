using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class RemoveCardFromHandSystem {
		public static void Update(World world) {
			var toProcess = new List<int>();

			int[] succeededReq = { TypeId<CardUse>.Value, TypeId<ActionSucceeded>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(succeededReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcess.Add(arch.Entities[i]); }
			}

			int[] failedReq = { TypeId<CardUse>.Value, TypeId<ActionFailed>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(failedReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcess.Add(arch.Entities[i]); }
			}

			foreach (int e in toProcess) {
				world.Remove<CardInHand>(e);
				world.Add(e, new CardDiscard());
			}
		}

		/// <summary>
		/// Debug cheat: discard a specific hand card so CheckHandSizeSystem can trigger a replacement draw.
		/// </summary>
		public static bool ForceDiscard(
			World world,
			string orgId,
			string countryId,
			string actionId,
			string targetCountryId,
			int slotIndex) {
			if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(actionId)) {
				return false;
			}

			int entity = FindHandCard(world, orgId, countryId, actionId, targetCountryId, slotIndex);
			if (entity < 0 || !world.Has<CardInHand>(entity)) {
				return false;
			}

			world.Remove<CardInHand>(entity);
			if (!world.Has<CardDiscard>(entity)) {
				world.Add(entity, new CardDiscard());
			}
			return true;
		}

		static int FindHandCard(
			World world,
			string orgId,
			string countryId,
			string actionId,
			string targetCountryId,
			int slotIndex) {
			CardOwnerKind ownerKind = string.IsNullOrEmpty(countryId) ? CardOwnerKind.Org : CardOwnerKind.Country;
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
						|| actions[i].ActionId != actionId
						|| hands[i].SlotIndex != slotIndex) {
						continue;
					}
					int entity = arch.Entities[i];
					string entityTarget = world.Has<RelationCardTarget>(entity)
						? world.Get<RelationCardTarget>(entity).TargetCountryId
						: world.Has<RevengeCardTarget>(entity) ? world.Get<RevengeCardTarget>(entity).TargetCountryId : "";
					if (entityTarget == (targetCountryId ?? "")) {
						return entity;
					}
				}
			}
			return -1;
		}
	}
}
