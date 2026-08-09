using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Systems {
	/// <summary>
	/// Cached country-relation service. Prefer SetRelation/RemoveRelation over direct
	/// <see cref="CountryRelation"/> component access so the cache stays coherent.
	/// </summary>
	public sealed class CountryRelations {
		// null value means the pair was resolved and has no relation (known absence).
		readonly Dictionary<(string A, string B), RelationKind?> _byPair =
			new Dictionary<(string A, string B), RelationKind?>();
		readonly Dictionary<string, List<string>> _friendsByCountry =
			new Dictionary<string, List<string>>(StringComparer.Ordinal);
		readonly Dictionary<string, List<string>> _rivalsByCountry =
			new Dictionary<string, List<string>>(StringComparer.Ordinal);
		readonly Dictionary<string, bool> _hasSuitableTarget =
			new Dictionary<string, bool>(StringComparer.Ordinal);

		public Action<string>? OnCacheMissWarning { get; set; }

		public bool SetRelation(World world, string countryIdA, string countryIdB, RelationKind kind) {
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
			PutCache(countryIdA, countryIdB, kind);
			BumpVersion(world);
			return true;
		}

		public bool RemoveRelation(World world, string countryIdA, string countryIdB) {
			if (countryIdA == countryIdB) {
				return false;
			}
			bool removed = false;
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (Matches(relations[i], countryIdA, countryIdB)) {
						world.Destroy(arch.Entities[i]);
						removed = true;
						break;
					}
				}
				if (removed) {
					break;
				}
			}
			DropCache(countryIdA, countryIdB);
			if (removed) {
				BumpVersion(world);
			}
			return removed;
		}

		public RelationKind? GetRelation(IReadOnlyWorld world, string countryIdA, string countryIdB) {
			if (countryIdA == countryIdB) {
				return null;
			}
			var key = Normalize(countryIdA, countryIdB);
			if (_byPair.TryGetValue(key, out RelationKind? cached)) {
				return cached;
			}
			RelationKind? fromWorld = ReadRelationFromWorld(world, countryIdA, countryIdB);
			if (fromWorld.HasValue) {
				OnCacheMissWarning?.Invoke(
					$"CountryRelations cache miss for '{countryIdA}'/'{countryIdB}'; falling back to world scan.");
				PutCache(countryIdA, countryIdB, fromWorld.Value);
			} else {
				_byPair[key] = null;
			}
			return fromWorld;
		}

		public (List<string> Friends, List<string> Rivals) GetRelationsByCountryId(
			IReadOnlyWorld world, string countryId) {
			EnsureCountryIndexes(world, countryId);
			var friends = _friendsByCountry.TryGetValue(countryId, out List<string>? friendList)
				? new List<string>(friendList)
				: new List<string>();
			var rivals = _rivalsByCountry.TryGetValue(countryId, out List<string>? rivalList)
				? new List<string>(rivalList)
				: new List<string>();
			return (friends, rivals);
		}

		public bool HasSuitableRelationTarget(IReadOnlyWorld world, string countryId) {
			if (_hasSuitableTarget.TryGetValue(countryId, out bool cached)) {
				return cached;
			}
			OnCacheMissWarning?.Invoke(
				$"CountryRelations HasSuitableRelationTarget cache miss for '{countryId}'; falling back to world scan.");
			EnsureCountryIndexes(world, countryId);
			var related = new HashSet<string>(StringComparer.Ordinal);
			if (_friendsByCountry.TryGetValue(countryId, out List<string>? friends)) {
				foreach (string otherId in friends) {
					related.Add(otherId);
				}
			}
			if (_rivalsByCountry.TryGetValue(countryId, out List<string>? rivals)) {
				foreach (string otherId in rivals) {
					related.Add(otherId);
				}
			}

			bool found = false;
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string candidateId = countries[i].CountryId;
					if (candidateId == countryId || related.Contains(candidateId)) {
						continue;
					}
					found = true;
					break;
				}
				if (found) {
					break;
				}
			}
			_hasSuitableTarget[countryId] = found;
			return found;
		}

		public bool MatchesCondition(
			IReadOnlyWorld world,
			string countryId,
			string targetCountryId,
			string relationKind) {
			return relationKind switch {
				"none" => string.IsNullOrEmpty(targetCountryId)
					? HasSuitableRelationTarget(world, countryId)
					: GetRelation(world, countryId, targetCountryId) == null,
				"friend" => GetRelation(world, countryId, targetCountryId) == RelationKind.Friend,
				"rival" => GetRelation(world, countryId, targetCountryId) == RelationKind.Rival,
				_ => throw new InvalidOperationException(
					$"Unsupported relation condition kind '{relationKind}'; expected none|friend|rival.")
			};
		}

		/// <summary>Rebuild the entire cache from world state (init/load).</summary>
		public void Rebuild(IReadOnlyWorld world) {
			_byPair.Clear();
			_friendsByCountry.Clear();
			_rivalsByCountry.Clear();
			_hasSuitableTarget.Clear();
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					PutCache(relations[i].LeftCountryId, relations[i].RightCountryId, relations[i].Kind);
				}
			}
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

		void EnsureCountryIndexes(IReadOnlyWorld world, string countryId) {
			if (_friendsByCountry.ContainsKey(countryId) || _rivalsByCountry.ContainsKey(countryId)) {
				return;
			}
			// Cold country with no cached edges — scan once and seed empty lists so we don't warn repeatedly.
			bool foundAny = false;
			int[] required = { TypeId<CountryRelation>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryRelation[] relations = arch.GetColumn<CountryRelation>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (relations[i].LeftCountryId == countryId || relations[i].RightCountryId == countryId) {
						foundAny = true;
						OnCacheMissWarning?.Invoke(
							$"CountryRelations index miss for '{countryId}'; falling back to world scan.");
						PutCache(relations[i].LeftCountryId, relations[i].RightCountryId, relations[i].Kind);
					}
				}
			}
			if (!foundAny) {
				_friendsByCountry[countryId] = new List<string>();
				_rivalsByCountry[countryId] = new List<string>();
			}
		}

		void PutCache(string countryIdA, string countryIdB, RelationKind kind) {
			var key = Normalize(countryIdA, countryIdB);
			if (_byPair.TryGetValue(key, out RelationKind? previous) && previous.HasValue) {
				RemoveFromIndex(key.A, key.B, previous.Value);
				RemoveFromIndex(key.B, key.A, previous.Value);
			}
			_byPair[key] = kind;
			AddToIndex(countryIdA, countryIdB, kind);
			AddToIndex(countryIdB, countryIdA, kind);
			InvalidateHasSuitable(countryIdA);
			InvalidateHasSuitable(countryIdB);
		}

		void DropCache(string countryIdA, string countryIdB) {
			var key = Normalize(countryIdA, countryIdB);
			if (_byPair.TryGetValue(key, out RelationKind? previous) && previous.HasValue) {
				RemoveFromIndex(key.A, key.B, previous.Value);
				RemoveFromIndex(key.B, key.A, previous.Value);
			}
			// Cache known absence so later GetRelation calls do not rescan the world.
			_byPair[key] = null;
			InvalidateHasSuitable(countryIdA);
			InvalidateHasSuitable(countryIdB);
		}

		void InvalidateHasSuitable(string countryId) {
			_hasSuitableTarget.Remove(countryId);
		}

		void AddToIndex(string countryId, string otherId, RelationKind kind) {
			Dictionary<string, List<string>> map = kind == RelationKind.Friend
				? _friendsByCountry
				: _rivalsByCountry;
			Dictionary<string, List<string>> otherMap = kind == RelationKind.Friend
				? _rivalsByCountry
				: _friendsByCountry;
			if (!map.TryGetValue(countryId, out List<string>? list)) {
				list = new List<string>();
				map[countryId] = list;
			}
			if (!list.Contains(otherId)) {
				list.Add(otherId);
			}
			if (otherMap.TryGetValue(countryId, out List<string>? conflicting)) {
				conflicting.Remove(otherId);
			}
		}

		void RemoveFromIndex(string countryId, string otherId, RelationKind kind) {
			Dictionary<string, List<string>> map = kind == RelationKind.Friend
				? _friendsByCountry
				: _rivalsByCountry;
			if (map.TryGetValue(countryId, out List<string>? list)) {
				list.Remove(otherId);
			}
		}

		static RelationKind? ReadRelationFromWorld(IReadOnlyWorld world, string countryIdA, string countryIdB) {
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

		static (string A, string B) Normalize(string countryIdA, string countryIdB) {
			return string.CompareOrdinal(countryIdA, countryIdB) <= 0
				? (countryIdA, countryIdB)
				: (countryIdB, countryIdA);
		}

		static bool Matches(CountryRelation relation, string countryIdA, string countryIdB) {
			return (relation.LeftCountryId == countryIdA && relation.RightCountryId == countryIdB)
				|| (relation.LeftCountryId == countryIdB && relation.RightCountryId == countryIdA);
		}
	}
}
