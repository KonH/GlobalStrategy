using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class RelationCardSyncSystem {
		public static void Update(World world, ActionConfig config) {
			int versionEntity = EnsureSingleton<CountryRelationsVersion>(world, new CountryRelationsVersion { Value = 0 });
			int syncStateEntity = EnsureSingleton<RelationCardSyncState>(world, new RelationCardSyncState { LastSyncedVersion = -1 });

			int currentVersion = world.Get<CountryRelationsVersion>(versionEntity).Value;
			ref RelationCardSyncState syncState = ref world.Get<RelationCardSyncState>(syncStateEntity);
			if (syncState.LastSyncedVersion == currentVersion) { return; }

			var countryDecks = new List<(string orgId, string countryId)>();
			int[] deckReq = { TypeId<CardDeck>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].CountryId != "") {
						countryDecks.Add((decks[i].OrgId, decks[i].CountryId));
					}
				}
			}

			foreach (var (orgId, countryId) in countryDecks) {
				var (friends, rivals) = CountryRelations.GetRelationsByCountryId(world, countryId);
				foreach (string otherCountryId in friends) {
					EnsureCardInstance(world, orgId, countryId, otherCountryId, RelationKind.Friend, "stop_friendship");
				}
				foreach (string otherCountryId in rivals) {
					EnsureCardInstance(world, orgId, countryId, otherCountryId, RelationKind.Rival, "stop_rivalry");
					EnsureCardInstance(world, orgId, countryId, otherCountryId, RelationKind.Rival, "declare_war");
				}
			}

			syncState.LastSyncedVersion = currentVersion;
		}

		static void EnsureCardInstance(World world, string orgId, string countryId, string targetCountryId, RelationKind kind, string actionId) {
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
