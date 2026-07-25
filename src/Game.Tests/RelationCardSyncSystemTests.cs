using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class RelationCardSyncSystemTests {
		static GameLogic BuildLogic(int? rngSeed = null) {
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: new List<string> { MultiOrgTestSupport.OrgA },
				rngSeed: rngSeed,
				includeCountryCard: true);
			return new GameLogic(ctx);
		}

		static List<(int Entity, string OrgId, string CountryId, string TargetCountryId, RelationKind Kind)> GetInstances(World world, string actionId) {
			var result = new List<(int, string, string, string, RelationKind)>();
			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<RelationCardTarget>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				RelationCardTarget[] targets = arch.GetColumn<RelationCardTarget>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId) {
						result.Add((arch.Entities[i], orgs[i].OrgId, countries[i].CountryId, targets[i].TargetCountryId, targets[i].Kind));
					}
				}
			}
			return result;
		}

		[Fact]
		void creates_one_stop_friendship_instance_for_an_existing_friend_relation() {
			var logic = BuildLogic();
			logic.Update(0f);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Friend);

			logic.Update(0f);

			var instances = GetInstances(logic.World, "stop_friendship");
			var matching = instances.FindAll(i => i.OrgId == MultiOrgTestSupport.OrgA && i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.HqB);
			Assert.Single(matching);
			Assert.Equal(RelationKind.Friend, matching[0].Kind);
		}

		[Fact]
		void second_tick_with_no_relation_change_does_not_duplicate() {
			var logic = BuildLogic();
			logic.Update(0f);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Friend);
			logic.Update(0f);

			logic.Update(0f);
			logic.Update(0f);

			var instances = GetInstances(logic.World, "stop_friendship");
			var matching = instances.FindAll(i => i.OrgId == MultiOrgTestSupport.OrgA && i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.HqB);
			Assert.Single(matching);
		}

		[Fact]
		void simultaneous_friend_and_rival_relations_produce_two_independent_entities() {
			var logic = BuildLogic();
			logic.Update(0f);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Friend);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.ExtraCountry1, RelationKind.Rival);

			logic.Update(0f);

			var friendInstances = GetInstances(logic.World, "stop_friendship");
			var rivalInstances = GetInstances(logic.World, "stop_rivalry");
			var friendMatching = friendInstances.FindAll(i => i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.HqB);
			var rivalMatching = rivalInstances.FindAll(i => i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.ExtraCountry1);
			Assert.Single(friendMatching);
			Assert.Single(rivalMatching);
			Assert.NotEqual(friendMatching[0].Entity, rivalMatching[0].Entity);
		}

		[Fact]
		void cleared_then_reformed_pair_reuses_the_same_entity() {
			var logic = BuildLogic();
			logic.Update(0f);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Friend);
			logic.Update(0f);
			var firstInstances = GetInstances(logic.World, "stop_friendship");
			var firstMatching = firstInstances.FindAll(i => i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.HqB);
			Assert.Single(firstMatching);
			int originalEntity = firstMatching[0].Entity;

			CountryRelations.RemoveRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB);
			logic.Update(0f);
			CountryRelations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Friend);
			logic.Update(0f);

			var finalInstances = GetInstances(logic.World, "stop_friendship");
			var finalMatching = finalInstances.FindAll(i => i.CountryId == MultiOrgTestSupport.HqA && i.TargetCountryId == MultiOrgTestSupport.HqB);
			Assert.Single(finalMatching);
			Assert.Equal(originalEntity, finalMatching[0].Entity);
		}

		[Fact]
		void freshly_initialized_world_seeds_version_and_sync_state_singletons() {
			var logic = BuildLogic();

			logic.Update(0f);

			int[] versionReq = { TypeId<CountryRelationsVersion>.Value };
			int[] syncReq = { TypeId<RelationCardSyncState>.Value };
			int versionCount = 0;
			foreach (var arch in logic.World.GetMatchingArchetypes(versionReq, null)) { versionCount += arch.Count; }
			int syncCount = 0;
			foreach (var arch in logic.World.GetMatchingArchetypes(syncReq, null)) { syncCount += arch.Count; }
			Assert.Equal(1, versionCount);
			Assert.Equal(1, syncCount);
		}
	}
}
