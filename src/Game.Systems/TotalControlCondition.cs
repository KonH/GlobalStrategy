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
			return GetCurrent(context) >= GetTarget(context);
		}

		public double GetCurrent(CompletionConditionContext context) {
			if (context.AvailableCountryIds.Count == 0) {
				return 0;
			}
			Dictionary<string, int> control = OrgMetrics.GetControlByCountry(
				context.World, context.OrganizationId, context.AvailableCountryIds);
			return GetCurrentFromControl(control);
		}

		public static double GetCurrentFromControl(IReadOnlyDictionary<string, int> control) {
			long totalControl = 0;
			foreach (int value in control.Values) {
				totalControl += value;
			}
			return totalControl;
		}

		public double GetTarget(CompletionConditionContext context) {
			return _threshold * context.AvailableCountryIds.Count * context.MaxControlPool;
		}
	}
}
