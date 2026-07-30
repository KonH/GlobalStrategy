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
			if (string.IsNullOrEmpty(countryId)) {
				int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
				int[] excludeCountry = { TypeId<CountryContext>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, excludeCountry)) {
					GameAction[] actions = arch.GetColumn<GameAction>();
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					CardInHand[] hands = arch.GetColumn<CardInHand>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId
							&& actions[i].ActionId == actionId
							&& hands[i].SlotIndex == slotIndex) {
							return arch.Entities[i];
						}
					}
				}
				return -1;
			}

			int[] countryReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId
						|| countries[i].CountryId != countryId
						|| actions[i].ActionId != actionId
						|| hands[i].SlotIndex != slotIndex) {
						continue;
					}
					int entity = arch.Entities[i];
					string entityTarget = world.Has<RelationCardTarget>(entity)
						? world.Get<RelationCardTarget>(entity).TargetCountryId
						: "";
					if (entityTarget == (targetCountryId ?? "")) {
						return entity;
					}
				}
			}
			return -1;
		}
	}
}
