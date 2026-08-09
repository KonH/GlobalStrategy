using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ActionCooldownQueryTests {
		static readonly DateTime Now = new DateTime(1880, 6, 1);

		static int AddCooldown(World world, string orgId, string actionId, DateTime endTime) {
			int entity = world.Create();
			world.Add(entity, new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime });
			return entity;
		}

		[Fact]
		void is_on_cooldown_false_when_no_tracking_entity_exists() {
			var world = new World();

			Assert.False(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
			Assert.Null(ActionCooldownQuery.GetRemaining(world, "OrgA", "declare_war", Now));
		}

		[Fact]
		void is_on_cooldown_true_when_end_time_is_after_current_time() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(7));

			Assert.True(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
		}

		[Fact]
		void is_on_cooldown_false_when_end_time_equals_current_time() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now);

			Assert.False(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
		}

		[Fact]
		void is_on_cooldown_false_when_end_time_is_before_current_time() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(-1));

			Assert.False(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
		}

		[Fact]
		void get_remaining_returns_correct_time_span_when_on_cooldown() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(7));

			TimeSpan? remaining = ActionCooldownQuery.GetRemaining(world, "OrgA", "declare_war", Now);

			Assert.NotNull(remaining);
			Assert.Equal(TimeSpan.FromDays(7), remaining!.Value);
		}

		[Fact]
		void get_remaining_returns_null_when_not_on_cooldown() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(-1));

			Assert.Null(ActionCooldownQuery.GetRemaining(world, "OrgA", "declare_war", Now));
		}

		[Fact]
		void different_action_ids_for_same_org_do_not_cross_contaminate() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(7));
			AddCooldown(world, "OrgA", "make_friend", Now.AddDays(-1));

			Assert.True(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
			Assert.False(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "make_friend", Now));
		}

		[Fact]
		void different_orgs_for_same_action_id_do_not_cross_contaminate() {
			var world = new World();
			AddCooldown(world, "OrgA", "declare_war", Now.AddDays(7));
			AddCooldown(world, "OrgB", "declare_war", Now.AddDays(-1));

			Assert.True(ActionCooldownQuery.IsOnCooldown(world, "OrgA", "declare_war", Now));
			Assert.False(ActionCooldownQuery.IsOnCooldown(world, "OrgB", "declare_war", Now));
		}
	}
}
