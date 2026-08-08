using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CheckHandSizeSystem {
		public static void Update(World world) {
			var discards = new List<(string orgId, CardOwnerKind ownerKind)>();
			int[] required = {
				TypeId<CardDiscard>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					discards.Add((orgs[i].OrgId, owners[i].Value));
				}
			}

			var processed = new HashSet<(string, CardOwnerKind)>();
			foreach (var (orgId, ownerKind) in discards) {
				if (!processed.Add((orgId, ownerKind))) { continue; }
				int handSize = GetHandSize(world, orgId, ownerKind);
				int currentHand = CountCardsInHand(world, orgId, ownerKind);
				if (currentHand < handSize) {
					AddCardDraw(world, orgId, ownerKind, handSize - currentHand);
				}
			}
		}

		static int GetHandSize(IReadOnlyWorld world, string orgId, CardOwnerKind ownerKind) {
			int[] required = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && owners[i].Value == ownerKind) {
						return hands[i].HandSize;
					}
				}
			}
			return ownerKind == CardOwnerKind.Org ? 1 : 3;
		}

		static int CountCardsInHand(IReadOnlyWorld world, string orgId, CardOwnerKind ownerKind) {
			int total = 0;
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId && owners[i].Value == ownerKind) { total++; }
				}
			}
			return total;
		}

		static void AddCardDraw(World world, string orgId, CardOwnerKind ownerKind, int count) {
			int[] required = { TypeId<CardDeck>.Value, TypeId<CardOwnerType>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && owners[i].Value == ownerKind) {
						world.Add(arch.Entities[i], new CardDraw { Count = count });
						return;
					}
				}
			}
		}
	}
}
