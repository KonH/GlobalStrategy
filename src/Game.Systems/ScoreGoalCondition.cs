using System;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class ScoreGoalCondition : ICompletionCondition {
		readonly double _goal;

		public ScoreGoalCondition(double goal) {
			if (double.IsNaN(goal) || double.IsInfinity(goal) || goal <= 0.0) {
				throw new ArgumentOutOfRangeException(nameof(goal), goal,
					"Score-goal completion threshold must be a positive finite value.");
			}
			_goal = goal;
		}

		public bool IsMet(CompletionConditionContext context) {
			return GetCurrent(context) >= GetTarget(context);
		}

		public double GetCurrent(CompletionConditionContext context) {
			return ResourceQuery.GetValue(context.World, context.OrganizationId, ResourceDefinitions.OrgScore);
		}

		public double GetTarget(CompletionConditionContext context) {
			return _goal;
		}
	}
}
