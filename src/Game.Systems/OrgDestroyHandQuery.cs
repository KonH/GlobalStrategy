using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class OrgDestroyHandQuery {
		public static IReadOnlyList<int> GetHandCardEntities(
			IReadOnlyWorld world,
			string orgId,
			CardOwnerKind ownerKind) {
			var entities = new List<int>();
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] orgContexts = archetype.GetColumn<OrgContext>();
				CardOwnerType[] owners = archetype.GetColumn<CardOwnerType>();
				for (int i = 0; i < archetype.Count; i++) {
					if (orgContexts[i].OrgId == orgId && owners[i].Value == ownerKind) {
						entities.Add(archetype.Entities[i]);
					}
				}
			}
			entities.Sort();
			return entities;
		}

		public static bool TryGetHandSize(
			IReadOnlyWorld world,
			string orgId,
			CardOwnerKind ownerKind,
			out int handCount,
			out int handSize) {
			handCount = GetHandCardEntities(world, orgId, ownerKind).Count;
			handSize = 0;
			bool found = false;
			int[] required = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value
			};
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				CardDeck[] decks = archetype.GetColumn<CardDeck>();
				CardOwnerType[] owners = archetype.GetColumn<CardOwnerType>();
				CardHand[] hands = archetype.GetColumn<CardHand>();
				for (int i = 0; i < archetype.Count; i++) {
					if (decks[i].OrgId != orgId || owners[i].Value != ownerKind) {
						continue;
					}
					if (found) {
						throw new InvalidOperationException(
							$"Multiple {ownerKind} card decks found for organization '{orgId}'.");
					}
					found = true;
					handSize = hands[i].HandSize;
				}
			}
			return found;
		}
	}
}
