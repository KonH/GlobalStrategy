using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class RevengeCardSyncSystem {
		public static void Update(World world, ActionConfig config) {
			ActionDefinition? definition = config.Find("declare_revenge_war");
			if (definition == null || definition.DeckCopies <= 0) { return; }

			var orgIds = new SortedSet<string>(StringComparer.Ordinal);
			int[] deckReq = { TypeId<CardDeck>.Value, TypeId<CardOwnerType>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].Value == CardOwnerKind.Country) {
						orgIds.Add(decks[i].OrgId);
					}
				}
			}

			var countryIds = new SortedSet<string>(StringComparer.Ordinal);
			int[] countryReq = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				Country[] countries = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) { countryIds.Add(countries[i].CountryId); }
			}

			foreach (string orgId in orgIds) {
				foreach (string countryId in countryIds) {
					foreach (string targetCountryId in RevengeEligibilityQuery.GetTargetCountryIds(world, countryId)) {
						EnsureCardInstance(world, orgId, countryId, targetCountryId);
					}
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
					if (actions[i].ActionId == "declare_revenge_war" && orgs[i].OrgId == orgId
						&& countries[i].CountryId == countryId && targets[i].TargetCountryId == targetCountryId) {
						return;
					}
				}
			}

			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = "declare_revenge_war" });
			world.Add(entity, new OrgContext { OrgId = orgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(entity, new CountryContext { CountryId = countryId });
			world.Add(entity, new RevengeCardTarget { TargetCountryId = targetCountryId });
		}
	}
}
