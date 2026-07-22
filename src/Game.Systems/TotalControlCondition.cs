using System;
using System.Collections.Generic;

namespace GS.Game.Systems {
	public sealed class TotalControlCondition : ICompletionCondition {
		readonly double _threshold;

		public TotalControlCondition(double threshold) {
			if (double.IsNaN(threshold) || double.IsInfinity(threshold) || threshold <= 0.0 || threshold > 1.0) {
				throw new ArgumentOutOfRangeException(nameof(threshold), threshold,
					"Total-control completion threshold must be greater than zero and at most one.");
			}
			_threshold = threshold;
		}

		public bool IsMet(CompletionConditionContext context) {
			if (context.AvailableCountryIds.Count == 0) {
				return false;
			}

			Dictionary<string, int> control = OrgMetrics.GetControlByCountry(
				context.World, context.OrganizationId, context.AvailableCountryIds);
			long totalControl = 0;
			foreach (int value in control.Values) {
				totalControl += value;
			}
			long totalCapacity = (long)context.AvailableCountryIds.Count * context.MaxControlPool;
			return totalCapacity > 0 && totalControl >= _threshold * totalCapacity;
		}
	}
}
