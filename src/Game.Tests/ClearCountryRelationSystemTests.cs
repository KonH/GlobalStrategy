using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ClearCountryRelationSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static int AddMarker(World world, string orgId, string countryId, string targetCountryId) {
			int e = world.Create();
			world.Add(e, new ClearCountryRelationEffect { EffectId = "clear_friendship_effect", OrgId = orgId, CountryId = countryId, TargetCountryId = targetCountryId });
			return e;
		}

		static List<RelationClearedApplied> GetRelationClearedApplied(World world) {
			var result = new List<RelationClearedApplied>();
			int[] req = { TypeId<RelationClearedApplied>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				RelationClearedApplied[] applied = arch.GetColumn<RelationClearedApplied>();
				for (int i = 0; i < arch.Count; i++) { result.Add(applied[i]); }
			}
			return result;
		}

		[Fact]
		void removes_the_named_relation() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);
			AddMarker(world, "OrgA", "A", "B");

			ClearCountryRelationSystem.Update(world, _relations);

			Assert.Null(_relations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void emits_relation_cleared_applied_matching_the_cleared_pair() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			_relations.SetRelation(world, "A", "B", RelationKind.Rival);
			AddMarker(world, "OrgA", "A", "B");

			ClearCountryRelationSystem.Update(world, _relations);

			var applied = GetRelationClearedApplied(world);
			Assert.Single(applied);
			Assert.Equal("OrgA", applied[0].OrgId);
			Assert.Equal("A", applied[0].CountryId);
			Assert.Equal("B", applied[0].TargetCountryId);
			Assert.Equal(RelationKind.Rival, applied[0].Kind);
		}

		[Fact]
		void does_not_destroy_the_marker_entity() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);
			int markerEntity = AddMarker(world, "OrgA", "A", "B");

			ClearCountryRelationSystem.Update(world, _relations);

			Assert.True(world.TryGet<ClearCountryRelationEffect>(markerEntity, out _));
		}

		[Fact]
		void leaves_unrelated_pairs_untouched() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			AddCountry(world, "C");
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);
			_relations.SetRelation(world, "A", "C", RelationKind.Rival);
			AddMarker(world, "OrgA", "A", "B");

			ClearCountryRelationSystem.Update(world, _relations);

			Assert.Null(_relations.GetRelation(world, "A", "B"));
			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "C"));
		}

		[Fact]
		void no_markers_is_a_safe_noop() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			_relations.SetRelation(world, "A", "B", RelationKind.Friend);

			ClearCountryRelationSystem.Update(world, _relations);

			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "A", "B"));
			Assert.Empty(GetRelationClearedApplied(world));
		}
	}
}
