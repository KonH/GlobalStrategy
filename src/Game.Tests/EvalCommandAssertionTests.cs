using System;
using System.Collections.Generic;
using GS.Game.Evals;
using Xunit;

namespace GS.Game.Tests {
	public class EvalCommandAssertionTests {
		static BatchRunner.RunOutcome Outcome(params (string featureId, string actionId)[] emissions) {
			return new BatchRunner.RunOutcome { Success = true, Emissions = emissions };
		}

		[Fact]
		void candidate_arm_without_feature_emission_fails_with_never_acted() {
			var runs = new List<BatchRunner.RunOutcome> { Outcome() };
			Assert.False(EmissionAssertions.CandidateArmActed(runs, "myFeature", Array.Empty<string>()));
		}

		[Fact]
		void emission_from_other_feature_with_same_action_id_does_not_satisfy_candidate_assertion() {
			var runs = new List<BatchRunner.RunOutcome> { Outcome(("baselineCardPlay", "discover_country")) };
			Assert.False(EmissionAssertions.CandidateArmActed(runs, "myFeature", Array.Empty<string>()));
		}

		[Fact]
		void target_actions_require_a_matching_attributed_emission() {
			var runsWithoutMatch = new List<BatchRunner.RunOutcome> { Outcome(("myFeature", "other_action")) };
			Assert.False(EmissionAssertions.CandidateArmActed(runsWithoutMatch, "myFeature", new[] { "discover_country" }));

			var runsWithMatch = new List<BatchRunner.RunOutcome> { Outcome(("myFeature", "discover_country")) };
			Assert.True(EmissionAssertions.CandidateArmActed(runsWithMatch, "myFeature", new[] { "discover_country" }));
		}

		[Fact]
		void baseline_arm_with_feature_emission_fails_batch() {
			var runs = new List<BatchRunner.RunOutcome> { Outcome(("myFeature", "discover_country")) };
			Assert.False(EmissionAssertions.BaselineArmClean(runs, "myFeature"));
		}
	}
}
