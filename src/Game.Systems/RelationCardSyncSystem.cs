using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class RelationCardSyncSystem {
		public static bool IsSyncedAction(string actionId) {
			return actionId == "stop_friendship"
				|| actionId == "stop_rivalry"
				|| actionId == "declare_war";
		}

		public static void Update(World world, CountryRelations relations, ActionConfig config) {
			int versionEntity = EnsureSingleton<CountryRelationsVersion>(world, new CountryRelationsVersion { Value = 0 });
			int syncStateEntity = EnsureSingleton<RelationCardSyncState>(world, new RelationCardSyncState { LastSyncedVersion = -1 });

			int currentVersion = world.Get<CountryRelationsVersion>(versionEntity).Value;
			ref RelationCardSyncState syncState = ref world.Get<RelationCardSyncState>(syncStateEntity);
			if (syncState.LastSyncedVersion == currentVersion) { return; }

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
					var (friends, rivals) = relations.GetRelationsByCountryId(world, countryId);
					foreach (string otherCountryId in friends) {
						EnsureCardInstance(world, config, orgId, countryId, otherCountryId, RelationKind.Friend, "stop_friendship");
					}
					foreach (string otherCountryId in rivals) {
						EnsureCardInstance(world, config, orgId, countryId, otherCountryId, RelationKind.Rival, "stop_rivalry");
						EnsureCardInstance(world, config, orgId, countryId, otherCountryId, RelationKind.Rival, "declare_war");
					}
				}
			}

			syncState.LastSyncedVersion = currentVersion;
		}

		static void EnsureCardInstance(
			World world,
			ActionConfig config,
			string orgId,
			string countryId,
			string targetCountryId,
			RelationKind kind,
			string actionId
		) {
			var def = config.Find(actionId);
			// Chance <= 0 means the card is disabled for draw; skip creating a deck instance.
			if (def == null || def.Chance <= 0) { return; }

			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<RelationCardTarget>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				RelationCardTarget[] targets = arch.GetColumn<RelationCardTarget>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId
						&& orgs[i].OrgId == orgId
						&& countries[i].CountryId == countryId
						&& targets[i].TargetCountryId == targetCountryId) {
						return;
					}
				}
			}

			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CardOwnerType(CardOwnerKind.Country));
			world.Add(e, new CountryContext { CountryId = countryId });
			world.Add(e, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = kind });
		}

		static int EnsureSingleton<T>(World world, T defaultValue) where T : struct {
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) { return arch.Entities[0]; }
			}
			int e = world.Create();
			world.Add(e, defaultValue);
			return e;
		}
	}
}
