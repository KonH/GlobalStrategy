using System;
using System.IO;
using GS.Configs.IO;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class EndGameThresholdFormulaTests {
		const double CalibrationMaximum = 1000.0;

		// The recorded calibration maximum from
		// .claude/skills/end-game-score-calibration/references/calibration_results.md — if a
		// future calibration rerun changes this value, the shipped config must be regenerated
		// and this constant updated in the same change, or the test below catches the drift.
		// Scaled by 0.8 (issue #112: score count goal) from the original 286971.0094511145.
		const double ShippedCalibrationMaximum = 229576.8075608916;

		const double ScoreGoal = 275592;

		// dotnet test runs from the test assembly's own output directory, not the repo root -
		// walk up until Assets/Configs is found.
		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}

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

		[Fact]
		void shipped_end_game_comparisons_match_formula_against_calibration_maximum() {
			GameSettings settings = new FileConfig<GameSettings>(FindRepoRootConfigPath("game_settings.json")).Load();

			Assert.Equal(9, settings.EndGameComparisons.Count);

			for (int i = 0; i < settings.EndGameComparisons.Count; i++) {
				double expected = Math.Round(Factor(i) * ShippedCalibrationMaximum, MidpointRounding.AwayFromZero);
				Assert.Equal(expected, settings.EndGameComparisons[i].Score);
			}
		}

		[Fact]
		void score_goal_equals_max_decreased_comparison_score_plus_100() {
			GameSettings settings = new FileConfig<GameSettings>(FindRepoRootConfigPath("game_settings.json")).Load();

			double maxComparisonScore = 0;
			foreach (var comparison in settings.EndGameComparisons) {
				if (comparison.Score > maxComparisonScore) {
					maxComparisonScore = comparison.Score;
				}
			}

			Assert.Equal(ScoreGoal, maxComparisonScore + 100);
		}
	}
}
