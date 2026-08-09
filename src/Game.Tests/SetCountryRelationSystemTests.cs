using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class SetCountryRelationSystemTests {
		readonly CountryRelations _relations = new CountryRelations();
		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static int AddMarker(World world, string orgId, string countryId, string targetCountryId, RelationKind kind) {
			int e = world.Create();
			world.Add(e, new SetCountryRelationEffect { EffectId = "make_friend_effect", OrgId = orgId, CountryId = countryId, TargetCountryId = targetCountryId, Kind = kind });
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
			int markerEntity = AddMarker(world, "OrgA", "A", "B", RelationKind.Friend);

			SetCountryRelationSystem.Update(world, _relations);

			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "A", "B"));
			Assert.False(world.TryGet<SetCountryRelationEffect>(markerEntity, out _));
			Assert.Equal(0, CountEntities<SetCountryRelationEffect>(world));
		}

		[Fact]
		void emits_relation_set_applied_matching_the_resolved_pair() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			AddMarker(world, "OrgA", "A", "B", RelationKind.Rival);

			SetCountryRelationSystem.Update(world, _relations);

			var applied = GetRelationSetApplied(world);
			Assert.Single(applied);
			Assert.Equal("OrgA", applied[0].OrgId);
			Assert.Equal("A", applied[0].CountryId);
			Assert.Equal("B", applied[0].TargetCountryId);
			Assert.Equal(RelationKind.Rival, applied[0].Kind);
			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void sets_the_relation_on_the_exact_named_target_even_when_other_candidates_exist() {
			var world = new World();
			AddCountry(world, "A");
			AddCountry(world, "B");
			AddCountry(world, "C");
			// B and C are both unrelated to A — a random/proximity pick could have chosen either.
			// There is no candidate search anymore: the marker's own TargetCountryId is authoritative.
			AddMarker(world, "OrgA", "A", "C", RelationKind.Rival);

			SetCountryRelationSystem.Update(world, _relations);

			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "C"));
			Assert.Null(_relations.GetRelation(world, "A", "B"));
		}
	}
}
