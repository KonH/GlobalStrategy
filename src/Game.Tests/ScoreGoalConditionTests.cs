using System;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ScoreGoalConditionTests {
		const string OrgA = "org-a";

		[Fact]
		void score_below_goal_is_not_met() {
			var world = new World();
			AddScore(world, OrgA, 99);
			var condition = new ScoreGoalCondition(100);

			Assert.False(condition.IsMet(Context(world)));
		}

		[Fact]
		void score_exactly_at_goal_is_met() {
			var world = new World();
			AddScore(world, OrgA, 100);
			var condition = new ScoreGoalCondition(100);

			Assert.True(condition.IsMet(Context(world)));
		}

		[Fact]
		void score_above_goal_is_met() {
			var world = new World();
			AddScore(world, OrgA, 101);
			var condition = new ScoreGoalCondition(100);

			Assert.True(condition.IsMet(Context(world)));
		}

		[Fact]
		void absent_score_resource_defaults_to_zero_and_is_not_met() {
			var condition = new ScoreGoalCondition(100);

			Assert.False(condition.IsMet(Context(new World())));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(-1)]
		[InlineData(double.NaN)]
		[InlineData(double.PositiveInfinity)]
		[InlineData(double.NegativeInfinity)]
		void constructor_rejects_non_positive_or_non_finite_goals(double goal) {
			Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreGoalCondition(goal));
		}

		static CompletionConditionContext Context(World world) {
			return new CompletionConditionContext(world, OrgA, new[] { "a" }, 100);
		}

		static void AddScore(World world, string orgId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(orgId));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.OrgScore, Value = value });
		}
	}
}
