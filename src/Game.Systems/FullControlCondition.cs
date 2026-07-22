using System;
using System.Collections.Generic;

namespace GS.Game.Systems {
	public sealed class FullControlCondition : ICompletionCondition {
		readonly int _requiredCountryCount;

		public FullControlCondition(double requiredCountryCount) {
			if (double.IsNaN(requiredCountryCount) || double.IsInfinity(requiredCountryCount)
				|| requiredCountryCount <= 0.0 || requiredCountryCount != Math.Truncate(requiredCountryCount)
				|| requiredCountryCount > int.MaxValue) {
				throw new ArgumentOutOfRangeException(nameof(requiredCountryCount), requiredCountryCount,
					"Full-control-country completion threshold must be a positive whole number.");
			}
			_requiredCountryCount = (int)requiredCountryCount;
		}

		public bool IsMet(CompletionConditionContext context) {
			if (context.AvailableCountryIds.Count == 0) {
				return false;
			}

			Dictionary<string, int> control = OrgMetrics.GetControlByCountry(
				context.World, context.OrganizationId, context.AvailableCountryIds);
			int fullCountryCount = 0;
			foreach (int value in control.Values) {
				if (value >= context.MaxControlPool) {
					fullCountryCount++;
				}
			}
			return fullCountryCount >= _requiredCountryCount;
		}
	}
}
