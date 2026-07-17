using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Evals {
	public static class EmissionAssertions {
		// Must-have command-on: at least one emission attributed to featureId across the
		// candidate arm's runs; when targetActions is non-empty, at least one attributed
		// emission's actionId must be in that set.
		public static bool CandidateArmActed(
			IEnumerable<BatchRunner.RunOutcome> candidateRuns, string featureId, IReadOnlyList<string> targetActions) {
			bool anyAttributed = false;
			bool anyMatchingTarget = false;
			foreach (var run in candidateRuns) {
				foreach (var emission in run.Emissions) {
					if (emission.FeatureId != featureId) { continue; }
					anyAttributed = true;
					if (targetActions.Count > 0 && targetActions.Contains(emission.ActionId)) {
						anyMatchingTarget = true;
					}
				}
			}
			return targetActions.Count > 0 ? anyMatchingTarget : anyAttributed;
		}

		// Must-have command-off: zero emissions attributed to featureId in any baseline run.
		public static bool BaselineArmClean(IEnumerable<BatchRunner.RunOutcome> baselineRuns, string featureId) {
			foreach (var run in baselineRuns) {
				foreach (var emission in run.Emissions) {
					if (emission.FeatureId == featureId) { return false; }
				}
			}
			return true;
		}
	}
}
