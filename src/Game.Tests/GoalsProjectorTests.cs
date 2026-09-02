using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	public class GoalsProjectorTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		const string OrgA = "org-a";
		const string OrgB = "org-b";

		static CompletionConditionConfig Leaf(string type, double value) {
			return new CompletionConditionConfig { Type = type, Value = value };
		}

		static CompletionConditionConfig Any(params CompletionConditionConfig[] members) {
			return new CompletionConditionConfig {
				Type = "any",
				Members = new List<CompletionConditionConfig>(members)
			};
		}

		static void SeedOrganization(World world, string orgId, double score) {
			int entity = world.Create();
			world.Add(entity, new Organization { OrganizationId = orgId, DisplayName = orgId });
			world.Add(entity, new ResourceOwner(orgId, OwnerType.Org));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.OrgScore, Value = score });
		}

		static void SeedCountry(World world, string countryId) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"{orgId}-{countryId}-{entity}"
			});
		}

		[Fact]
		void flattens_any_members_into_three_rows_in_config_order() {
			var leaves = GoalsProjector.FlattenLeaves(Any(
				Leaf("total_control", 0.8),
				Leaf("full_control_countries", 15),
				Leaf("score_goal", 270000)));

			Assert.Equal(3, leaves.Count);
			Assert.Equal(WinConditionHintKind.TotalControl, leaves[0].Kind);
			Assert.Equal(0.8, leaves[0].ConfigValue);
			Assert.Equal(WinConditionHintKind.FullControlCountries, leaves[1].Kind);
			Assert.Equal(15, leaves[1].ConfigValue);
			Assert.Equal(WinConditionHintKind.ScoreGoal, leaves[2].Kind);
			Assert.Equal(270000, leaves[2].ConfigValue);
		}

		[Fact]
		void per_org_currents_differ_while_targets_are_shared() {
			var world = new World();
			SeedCountry(world, "a");
			SeedCountry(world, "b");
			SeedOrganization(world, OrgA, 1000);
			SeedOrganization(world, OrgB, 500);
			AddControl(world, OrgA, "a", 100);
			AddControl(world, OrgA, "b", 60);
			AddControl(world, OrgB, "a", 50);

			var leaves = GoalsProjector.FlattenLeaves(Any(
				Leaf("total_control", 0.8),
				Leaf("full_control_countries", 1),
				Leaf("score_goal", 2000)));
			List<GoalsOrgEntryState> orgs = GoalsProjector.Build(world, leaves, 100, _resources);

			Assert.Equal(2, orgs.Count);
			GoalsOrgEntryState orgA = orgs.Find(o => o.OrgId == OrgA)!;
			GoalsOrgEntryState orgB = orgs.Find(o => o.OrgId == OrgB)!;

			Assert.Equal(160, orgA.Goals[0].Current);
			Assert.Equal(50, orgB.Goals[0].Current);
			Assert.Equal(orgA.Goals[0].Target, orgB.Goals[0].Target);
			Assert.Equal(160, orgA.Goals[0].Target);

			Assert.Equal(1, orgA.Goals[1].Current);
			Assert.Equal(0, orgB.Goals[1].Current);
			Assert.Equal(1, orgA.Goals[1].Target);
			Assert.Equal(1, orgB.Goals[1].Target);

			Assert.Equal(1000, orgA.Goals[2].Current);
			Assert.Equal(500, orgB.Goals[2].Current);
			Assert.Equal(2000, orgA.Goals[2].Target);
			Assert.Equal(2000, orgB.Goals[2].Target);
		}

		[Fact]
		void org_list_covers_all_seeded_orgs() {
			var world = new World();
			SeedCountry(world, "a");
			SeedOrganization(world, OrgA, 1);
			SeedOrganization(world, OrgB, 2);
			var leaves = GoalsProjector.FlattenLeaves(Leaf("score_goal", 10));

			List<GoalsOrgEntryState> orgs = GoalsProjector.Build(world, leaves, 100, _resources);

			Assert.Equal(2, orgs.Count);
			Assert.Contains(orgs, o => o.OrgId == OrgA);
			Assert.Contains(orgs, o => o.OrgId == OrgB);
		}

		[Fact]
		void last_org_standing_projects_destroyed_opponents_and_omits_destroyed_rows() {
			var world = new World();
			SeedCountry(world, "a");
			SeedOrganization(world, OrgA, 1);
			SeedOrganization(world, OrgB, 2);
			int orgC = world.Create();
			world.Add(orgC, new Organization { OrganizationId = "org-c" });
			world.Add(orgC, new IsOrgDestroyed());
			var leaves = GoalsProjector.FlattenLeaves(Leaf("last_org_standing", 0));

			List<GoalsOrgEntryState> orgs = GoalsProjector.Build(world, leaves, 100, _resources);

			Assert.Equal(2, orgs.Count);
			Assert.DoesNotContain(orgs, org => org.OrgId == "org-c");
			Assert.All(orgs, org => {
				Assert.Equal(WinConditionHintKind.LastOrgStanding, org.Goals[0].Kind);
				Assert.Equal(1, org.Goals[0].Current);
				Assert.Equal(2, org.Goals[0].Target);
			});
		}

		[Fact]
		void identical_set_does_not_notify() {
			var state = new GoalsState();
			int notifications = 0;
			state.PropertyChanged += (_, __) => notifications++;

			var entry = new GoalsOrgEntryState(OrgA, new List<GoalProgressEntryState> {
				new GoalProgressEntryState(WinConditionHintKind.ScoreGoal, 100, 50, 100, 2)
			});
			state.Set(new List<GoalsOrgEntryState> { entry });
			Assert.Equal(1, notifications);

			state.Set(new List<GoalsOrgEntryState> {
				new GoalsOrgEntryState(OrgA, new List<GoalProgressEntryState> {
					new GoalProgressEntryState(WinConditionHintKind.ScoreGoal, 100, 50, 100, 2)
				})
			});
			Assert.Equal(1, notifications);
		}

		[Fact]
		void fill_inputs_support_partial_and_over_target_ratios() {
			var world = new World();
			SeedCountry(world, "a");
			SeedOrganization(world, OrgA, 3000);
			AddControl(world, OrgA, "a", 40);
			var leaves = GoalsProjector.FlattenLeaves(Any(
				Leaf("total_control", 0.8),
				Leaf("score_goal", 2000)));

			GoalsOrgEntryState org = GoalsProjector.Build(world, leaves, 100, _resources)[0];
			Assert.True(org.Goals[0].Current / org.Goals[0].Target < 1.0);
			Assert.True(org.Goals[1].Current / org.Goals[1].Target > 1.0);
		}

		[Fact]
		void pull_entry_point_projects_from_completion_condition_config() {
			var world = new World();
			SeedCountry(world, "a");
			SeedOrganization(world, OrgA, 40);
			AddControl(world, OrgA, "a", 50);
			var config = Any(Leaf("total_control", 0.5), Leaf("score_goal", 100));

			List<GoalsOrgEntryState> orgs = GoalsProjector.Project(world, config, 100, _resources);

			Assert.Single(orgs);
			Assert.Equal(2, orgs[0].Goals.Count);
			Assert.Equal(50, orgs[0].Goals[0].Current);
			Assert.Equal(50, orgs[0].Goals[0].Target);
			Assert.Equal(40, orgs[0].Goals[1].Current);
			Assert.Equal(100, orgs[0].Goals[1].Target);
		}
	}
}
