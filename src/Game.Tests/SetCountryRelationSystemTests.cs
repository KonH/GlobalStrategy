using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class SetCountryRelationSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static int AddMarker(World world, string orgId, string countryId, RelationKind kind) {
			int e = world.Create();
			world.Add(e, new SetCountryRelationEffect { EffectId = "make_friend_effect", OrgId = orgId, CountryId = countryId, Kind = kind });
			return e;
		}

		static int CountEntities<T>(World world) {
			int count = 0;
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				count += arch.Count;
			}
			return count;
		}

		static List<RelationSetApplied> GetRelationSetApplied(World world) {
			var result = new List<RelationSetApplied>();
			int[] req = { TypeId<RelationSetApplied>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				RelationSetApplied[] applied = arch.GetColumn<RelationSetApplied>();
				for (int i = 0; i < arch.Count; i++) { result.Add(applied[i]); }
			}
			return result;
		}

		[Fact]
		void resolves_a_relation_of_the_requested_kind_and_destroys_the_marker() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			int markerEntity = AddMarker(world, "OrgA", "A", RelationKind.Friend);

			SetCountryRelationSystem.Update(world, _relations, -1, new Random(1));

			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "A", "B"));
			Assert.False(world.TryGet<SetCountryRelationEffect>(markerEntity, out _));
			Assert.Equal(0, CountEntities<SetCountryRelationEffect>(world));
		}

		[Fact]
		void emits_relation_set_applied_matching_the_resolved_pair() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			AddMarker(world, "OrgA", "A", RelationKind.Rival);

			SetCountryRelationSystem.Update(world, _relations, -1, new Random(1));

			var applied = GetRelationSetApplied(world);
			Assert.Single(applied);
			Assert.Equal("OrgA", applied[0].OrgId);
			Assert.Equal("A", applied[0].CountryId);
			Assert.Equal("B", applied[0].TargetCountryId);
			Assert.Equal(RelationKind.Rival, applied[0].Kind);
			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void excludes_source_country_and_countries_already_related() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			AddCountry(world, "C");
			// B is already a Friend of A, so only C is a suitable candidate.
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);
			AddMarker(world, "OrgA", "A", RelationKind.Rival);

			SetCountryRelationSystem.Update(world, _relations, -1, new Random(1));

			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "C"));
			// The pre-existing Friend relation with B must be untouched.
			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void no_suitable_candidate_is_a_safe_noop() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			// Every other available country is already related to A -> no suitable candidate.
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);
			int markerEntity = AddMarker(world, "OrgA", "A", RelationKind.Rival);

			SetCountryRelationSystem.Update(world, _relations, -1, new Random(1));

			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "A", "B"));
			Assert.False(world.TryGet<SetCountryRelationEffect>(markerEntity, out _));
			Assert.Empty(GetRelationSetApplied(world));
		}

		[Fact]
		void proximity_weighting_favors_the_nearer_candidate_over_many_runs() {
			int nearChosenCount = 0;
			const int trials = 200;
			for (int seed = 0; seed < trials; seed++) {
				var world = new World();
				AddCountry(world, "Anchor");
				AddCountry(world, "Near");
				AddCountry(world, "Far");

				var pmEntity = world.Create();
				var distances = new Dictionary<(string, string), float> {
					[Order("Anchor", "Near")] = 0.001f,
					[Order("Anchor", "Far")] = 1000f,
					[Order("Near", "Far")] = 1000f
				};
				world.Add(pmEntity, new ProximityMapData { Distances = distances });

				AddMarker(world, "OrgA", "Anchor", RelationKind.Friend);
				SetCountryRelationSystem.Update(world, _relations, pmEntity, new Random(seed));

				if (_relations.GetRelation(world, "Anchor", "Near") == RelationKind.Friend) {
					nearChosenCount++;
				}
			}

			// Expected share ~= 0.5 (uniform half, 50/50) + 0.5 (proximity half, heavily weighted to Near)
			// ~= 0.75. Assert it's clearly above the 0.5 a pure coin-flip-only pick would produce.
			Assert.True(nearChosenCount > trials * 0.6, $"Near candidate was chosen {nearChosenCount}/{trials} times, expected clear majority.");
		}

		static (string, string) Order(string a, string b) {
			return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
		}
	}
}
