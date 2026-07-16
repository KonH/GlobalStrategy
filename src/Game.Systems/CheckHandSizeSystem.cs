using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CheckHandSizeSystem {
		public static void Update(World world) {
			var discards = new List<(string orgId, string countryId)>();

			int[] discardOrgReq = { TypeId<CardDiscard>.Value, TypeId<OrgContext>.Value };
			int[] excludeCountry = { TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(discardOrgReq, excludeCountry)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					discards.Add((orgs[i].OrgId, ""));
				}
			}

			int[] discardCountryReq = { TypeId<CardDiscard>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(discardCountryReq, null)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					discards.Add((orgs[i].OrgId, countries[i].CountryId));
				}
			}

			if (discards.Count == 0) { return; }

			var processed = new HashSet<(string, string)>();
			foreach (var (orgId, countryId) in discards) {
				var key = (orgId, countryId);
				if (!processed.Add(key)) { continue; }

				int handSize = GetHandSize(world, orgId, countryId);
				int currentHand = CountCardsInHand(world, orgId, countryId);
				if (currentHand < handSize) {
					AddCardDraw(world, orgId, countryId, handSize - currentHand);
				}
			}
		}

		static int GetHandSize(World world, string orgId, string countryId) {
			int[] deckReq = { TypeId<CardDeck>.Value, TypeId<CardHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (decks[i].OrgId == orgId && decks[i].CountryId == countryId) {
						return hands[i].HandSize;
					}
				}
			}
			return string.IsNullOrEmpty(countryId) ? 1 : 3;
		}

		static int CountCardsInHand(World world, string orgId, string countryId) {
			int total = 0;
			if (string.IsNullOrEmpty(countryId)) {
				int[] req = { TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
				int[] excludeCountry = { TypeId<CountryContext>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, excludeCountry)) {
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId) { total++; }
					}
				}
			} else {
				int[] req = { TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					CountryContext[] countries = arch.GetColumn<CountryContext>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId && countries[i].CountryId == countryId) { total++; }
					}
				}
			}
			return total;
		}

		static void AddCardDraw(World world, string orgId, string countryId, int count) {
			int[] deckReq = { TypeId<CardDeck>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				int dc = arch.Count;
				for (int i = 0; i < dc; i++) {
					if (decks[i].OrgId == orgId && decks[i].CountryId == countryId) {
						world.Add(arch.Entities[i], new CardDraw { Count = count });
						return;
					}
				}
			}
			int drawEntity = world.Create();
			world.Add(drawEntity, new OrgContext { OrgId = orgId });
			if (!string.IsNullOrEmpty(countryId)) {
				world.Add(drawEntity, new CountryContext { CountryId = countryId });
			}
			world.Add(drawEntity, new CardDraw { Count = count });
		}
	}
}
