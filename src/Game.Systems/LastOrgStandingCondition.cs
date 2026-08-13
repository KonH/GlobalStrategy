using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public sealed class LastOrgStandingCondition : ICompletionCondition {
		public bool IsMet(CompletionConditionContext context) {
			var availableOrgIds = GameCompletionSystem.GetAvailableOrgIds(context.World);
			return CountOrganizations(context) > 1
				&& availableOrgIds.Count == 1
				&& availableOrgIds.Contains(context.OrganizationId);
		}

		static int CountOrganizations(CompletionConditionContext context) {
			int total = 0;
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in context.World.GetMatchingArchetypes(required, null)) {
				total += archetype.Count;
			}
			return total;
		}
	}
}
