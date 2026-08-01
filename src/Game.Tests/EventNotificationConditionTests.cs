using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class EventNotificationConditionTests {
		static ExpressionNode GtControlZero() => EventNotificationSettings.CreateGtControlZero();

		static void AddControl(World world, string orgId, string countryId, int value) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"base_{orgId}_{countryId}"
			});
		}

		[Fact]
		void null_condition_always_passes() {
			var world = new World();
			Assert.True(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, null));
		}

		[Fact]
		void gt_control_zero_fails_when_both_participants_have_zero() {
			var world = new World();
			AddControl(world, "Org", "A", 0);
			AddControl(world, "Org", "B", 0);

			Assert.False(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, GtControlZero()));
		}

		[Fact]
		void gt_control_zero_fails_when_no_control_entities() {
			var world = new World();
			Assert.False(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, GtControlZero()));
		}

		[Fact]
		void gt_control_zero_passes_when_attacker_has_influence() {
			var world = new World();
			AddControl(world, "Org", "A", 5);

			Assert.True(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, GtControlZero()));
		}

		[Fact]
		void gt_control_zero_passes_when_defender_has_influence() {
			var world = new World();
			AddControl(world, "Org", "B", 3);

			Assert.True(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, GtControlZero()));
		}

		[Fact]
		void gt_control_zero_does_not_require_control_in_both() {
			var world = new World();
			AddControl(world, "Org", "A", 1);

			Assert.True(EventNotificationConditionContext.Passes(
				world, "Org", new[] { "A", "B" }, GtControlZero()));
		}

		[Fact]
		void empty_player_org_fails_influence_condition() {
			var world = new World();
			AddControl(world, "Org", "A", 10);
			AddControl(world, "Org", "B", 10);

			Assert.False(EventNotificationConditionContext.Passes(
				world, "", new[] { "A", "B" }, GtControlZero()));
		}
	}
}
