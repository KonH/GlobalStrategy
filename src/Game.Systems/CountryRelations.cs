using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CountryRelations {
		public static bool SetRelation(World world, string countryIdA, string countryIdB, RelationKind kind) {
			if (countryIdA == countryIdB) {
				return false;
			}
			RemoveRelation(world, countryIdA, countryIdB);
			int entity = world.Create();
			world.Add(entity, new CountryRelation {
				Kind = kind,
				LeftCountryId = countryIdA,
				RightCountryId = countryIdB
			});
			BumpVersion(world);
			return true;
		}

		public static bool RemoveRelation(World world, string countryIdA, string countryIdB) {
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (Matches(relations[i], countryIdA, countryIdB)) {
						world.Destroy(arch.Entities[i]);
						BumpVersion(world);
						return true;
					}
				}
			}
			return false;
		}

		static void BumpVersion(World world) {
			int[] required = { TypeId<CountryRelationsVersion>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					CountryRelationsVersion[] versions = arch.GetColumn<CountryRelationsVersion>();
					versions[0].Value++;
					return;
				}
			}
		}

		public static RelationKind? GetRelation(IReadOnlyWorld world, string countryIdA, string countryIdB) {
			if (countryIdA == countryIdB) {
				return null;
			}
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (Matches(relations[i], countryIdA, countryIdB)) {
						return relations[i].Kind;
					}
				}
			}
			return null;
		}

		public static (List<string> Friends, List<string> Rivals) GetRelationsByCountryId(IReadOnlyWorld world, string countryId) {
			var friends = new List<string>();
			var rivals = new List<string>();
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string otherId;
					if (relations[i].LeftCountryId == countryId) {
						otherId = relations[i].RightCountryId;
					} else if (relations[i].RightCountryId == countryId) {
						otherId = relations[i].LeftCountryId;
					} else {
						continue;
					}
					if (relations[i].Kind == RelationKind.Friend) {
						friends.Add(otherId);
					} else {
						rivals.Add(otherId);
					}
				}
			}
			return (friends, rivals);
		}

		public static List<string> GetSuitableRelationCandidates(IReadOnlyWorld world, string countryId) {
			var result = new List<string>();
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string candidateId = countries[i].CountryId;
					if (candidateId == countryId) { continue; }
					if (GetRelation(world, countryId, candidateId) == null) {
						result.Add(candidateId);
					}
				}
			}
			return result;
		}

		public static bool HasSuitableRelationTarget(IReadOnlyWorld world, string countryId) {
			return GetSuitableRelationCandidates(world, countryId).Count > 0;
		}

		static bool Matches(CountryRelation relation, string countryIdA, string countryIdB) {
			return (relation.LeftCountryId == countryIdA && relation.RightCountryId == countryIdB)
				|| (relation.LeftCountryId == countryIdB && relation.RightCountryId == countryIdA);
		}
	}
}
