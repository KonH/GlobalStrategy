using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class RevengeCardSyncSystem {
		public static void Update(World world, ActionConfig config) {
			ActionDefinition? definition = config.Find("revenge");
			if (definition == null || definition.DeckCopies <= 0) { return; }

			var decks = new List<(string orgId, string countryId)>();
			int[] deckReq = { TypeId<CardDeck>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] cards = arch.GetColumn<CardDeck>();
				for (int i = 0; i < arch.Count; i++) {
					if (!string.IsNullOrEmpty(cards[i].CountryId)) {
						decks.Add((cards[i].OrgId, cards[i].CountryId));
					}
				}
			}

			foreach (var (orgId, countryId) in decks) {
				foreach (string targetCountryId in RevengeEligibilityQuery.GetTargetCountryIds(world, countryId)) {
					EnsureCardInstance(world, orgId, countryId, targetCountryId);
				}
			}
		}

		static void EnsureCardInstance(World world, string orgId, string countryId, string targetCountryId) {
			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<RevengeCardTarget>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				RevengeCardTarget[] targets = arch.GetColumn<RevengeCardTarget>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == "revenge" && orgs[i].OrgId == orgId
						&& countries[i].CountryId == countryId && targets[i].TargetCountryId == targetCountryId) {
						return;
					}
				}
			}

			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = "revenge" });
			world.Add(entity, new OrgContext { OrgId = orgId });
			world.Add(entity, new CountryContext { CountryId = countryId });
			world.Add(entity, new RevengeCardTarget { TargetCountryId = targetCountryId });
		}
	}
}
