using System.Collections.Generic;
using GS.Game.Evals;
using Xunit;

namespace GS.Game.Tests {
	public class EvalGateTests {
		[Fact]
		void mean_delta_exactly_at_negative_epsilon_passes() {
			Assert.True(GateEvaluator.ScoreGatePasses(-2.0, 2.0));
		}

		[Fact]
		void mean_delta_below_negative_epsilon_fails() {
			Assert.False(GateEvaluator.ScoreGatePasses(-2.01, 2.0));
		}

		[Fact]
		void epsilon_scales_with_baseline_mean() {
			var largeBaseline = new List<double> { 1000, 1000 };
			var smallBaseline = new List<double> { 10, 10 };
			double epsilonLarge = GateEvaluator.ComputeEpsilon(largeBaseline, 0.02, 0);
			double epsilonSmall = GateEvaluator.ComputeEpsilon(smallBaseline, 0.02, 0);

			// Same delta (-15) passes under the large-baseline epsilon, fails under the small one.
			Assert.True(GateEvaluator.ScoreGatePasses(-15, epsilonLarge));
			Assert.False(GateEvaluator.ScoreGatePasses(-15, epsilonSmall));
		}

		[Fact]
		void epsilon_absolute_floor_applies_when_baseline_mean_is_zero() {
			var zeroBaseline = new List<double> { 0, 0 };
			double epsilon = GateEvaluator.ComputeEpsilon(zeroBaseline, 0.02, 0.5);
			Assert.Equal(0.5, epsilon);
			Assert.True(GateEvaluator.ScoreGatePasses(-0.5, epsilon));
			Assert.False(GateEvaluator.ScoreGatePasses(-0.51, epsilon));
		}

		[Fact]
		void improved_flag_true_only_when_mean_delta_positive() {
			Assert.False(GateEvaluator.ImprovedFlag(0));
			Assert.False(GateEvaluator.ImprovedFlag(-0.1));
			Assert.True(GateEvaluator.ImprovedFlag(0.1));
		}

		[Fact]
		void reported_statistics_match_synthetic_deltas() {
			var baseline = new List<double> { 10, 10, 10, 10, 10 };
			var candidate = new List<double> { 12, 8, 10, 15, 9 };
			// deltas: 2, -2, 0, 5, -1
			var stats = GateEvaluator.ComputeStatistics(baseline, candidate);

			Assert.Equal(0.8, stats.Mean, 3);
			Assert.Equal(0.0, stats.Median, 3);
			Assert.Equal(-2.0, stats.Min, 3);
			Assert.Equal(5.0, stats.Max, 3);
			Assert.Equal(2, stats.Improved);
			Assert.Equal(2, stats.Worsened);
			Assert.Equal(1, stats.Unchanged);
		}
	}
}
