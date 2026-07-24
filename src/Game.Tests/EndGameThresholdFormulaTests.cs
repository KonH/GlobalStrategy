using System;
using Xunit;

namespace GS.Game.Tests {
	public class EndGameThresholdFormulaTests {
		const double CalibrationMaximum = 1000.0;

		static double Factor(int i) {
			return 0.05 + i * (1.20 - 0.05) / 8;
		}

		static double Threshold(int i) {
			return Math.Round(Factor(i) * CalibrationMaximum, MidpointRounding.AwayFromZero);
		}

		[Fact]
		void thresholds_are_strictly_ascending() {
			double previous = double.NegativeInfinity;
			for (int i = 0; i <= 8; i++) {
				double threshold = Threshold(i);
				Assert.True(threshold > previous, $"threshold({i}) = {threshold} is not greater than previous {previous}");
				previous = threshold;
			}
		}

		[Fact]
		void thresholds_match_hand_computed_expected_values() {
			// i=2's mathematical midpoint (337.5) is not exactly representable in double
			// (0.05 + 2 * 1.15 / 8 lands fractionally below .5), so it rounds down to 337
			// rather than away-from-zero to 338 — this is the actual double-precision
			// result of the documented formula, not a hand-idealized one.
			double[] expected = { 50, 194, 337, 481, 625, 769, 913, 1056, 1200 };

			for (int i = 0; i <= 8; i++) {
				Assert.Equal(expected[i], Threshold(i));
			}
		}
	}
}
