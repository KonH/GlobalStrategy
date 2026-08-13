using System.Collections.Generic;
using System.Linq;
using GS.Game.Evals;
using Xunit;

namespace GS.Game.Tests {
	public class EvalBatchTests {
		[Fact]
		void seeds_derive_from_base_seed_deterministically() {
			var seeds = SeedDerivation.Seeds(1880, 5);
			Assert.Equal(new List<int> { 1880, 1881, 1882, 1883, 1884 }, seeds);
		}

		[Fact]
		void baseline_arm_runs_once_per_batch_and_is_shared_across_parameter_sets() {
			var seeds = SeedDerivation.Seeds(1880, 3);
			var parameterSets = new List<ParameterSet> {
				new ParameterSet { Index = 0, Parameters = new Dictionary<string, double>() },
				new ParameterSet { Index = 1, Parameters = new Dictionary<string, double>() }
			};

			int baselineCalls = 0;
			var result = BatchRunner.Run(seeds, parameterSets, request => {
				if (request.Arm == "baseline") { baselineCalls++; }
				return new BatchRunner.RunOutcome { Success = true, Score = 1.0 };
			});

			Assert.Equal(3, baselineCalls);
			Assert.Equal(3, result.BaselineRuns.Count);
			Assert.Equal(2, result.ParameterSets.Count);
			Assert.All(result.ParameterSets, p => Assert.Equal(3, p.CandidateRuns.Count));
		}

		[Fact]
		void failing_run_fails_batch_naming_seed_arm_and_parameter_set() {
			var seeds = SeedDerivation.Seeds(1880, 2);
			var parameterSets = new List<ParameterSet> {
				new ParameterSet { Index = 0, Parameters = new Dictionary<string, double>() }
			};

			var ex = Assert.Throws<BatchRunner.BatchFailure>(() => {
				BatchRunner.Run(seeds, parameterSets, request => {
					if (request.Arm == "candidate" && request.Seed == 1881) {
						return new BatchRunner.RunOutcome { Success = false, Error = "boom" };
					}
					return new BatchRunner.RunOutcome { Success = true, Score = 1.0 };
				});
			});

			Assert.Contains("1881", ex.Message);
			Assert.Contains("candidate", ex.Message);
			Assert.Contains("parameterSet=0", ex.Message);
		}

		[Fact]
		void baseline_and_candidate_arms_differ_only_in_the_feature_enabled_flag() {
			var candidateFeatures = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "baselineCardPlay", Enabled = true },
				new BotFeatureConfigEntry { FeatureId = "myFeature", Enabled = true, Parameters = new Dictionary<string, double> { ["x"] = 1.0 } }
			};

			var baselineFeatures = EvalConfig.BuildBaselineFeatures(candidateFeatures, "myFeature");

			Assert.Equal(candidateFeatures.Count, baselineFeatures.Count);
			Assert.True(candidateFeatures.First(f => f.FeatureId == "myFeature").Enabled);
			Assert.False(baselineFeatures.First(f => f.FeatureId == "myFeature").Enabled);
			Assert.True(baselineFeatures.First(f => f.FeatureId == "baselineCardPlay").Enabled);
			// Parameters are preserved (feature is disabled, not stripped of its config).
			Assert.Equal(1.0, baselineFeatures.First(f => f.FeatureId == "myFeature").Parameters["x"]);
		}

		[Fact]
		void control_only_baseline_disables_non_control_and_preserves_parameters() {
			var candidateFeatures = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "control", Enabled = true, Parameters = new Dictionary<string, double> { ["a"] = 1.0 } },
				new BotFeatureConfigEntry { FeatureId = "warUnlock", Enabled = true, Parameters = new Dictionary<string, double> { ["b"] = 2.0 } },
				new BotFeatureConfigEntry { FeatureId = "warDeclare", Enabled = true, Parameters = new Dictionary<string, double> { ["c"] = 3.0 } }
			};

			var baselineFeatures = EvalConfig.BuildBaselineFeatures(
				candidateFeatures, "warDeclare", EvalConfig.BaselineModeControlOnly);

			Assert.Equal(3, baselineFeatures.Count);
			Assert.True(baselineFeatures.First(f => f.FeatureId == "control").Enabled);
			Assert.False(baselineFeatures.First(f => f.FeatureId == "warUnlock").Enabled);
			Assert.False(baselineFeatures.First(f => f.FeatureId == "warDeclare").Enabled);
			Assert.Equal(2.0, baselineFeatures.First(f => f.FeatureId == "warUnlock").Parameters["b"]);
			Assert.Equal(3.0, baselineFeatures.First(f => f.FeatureId == "warDeclare").Parameters["c"]);
		}

		[Fact]
		void feature_off_baseline_mode_matches_default_build() {
			var candidateFeatures = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "control", Enabled = true },
				new BotFeatureConfigEntry { FeatureId = "warUnlock", Enabled = true }
			};

			var defaultBaseline = EvalConfig.BuildBaselineFeatures(candidateFeatures, "warUnlock");
			var explicitFeatureOff = EvalConfig.BuildBaselineFeatures(
				candidateFeatures, "warUnlock", EvalConfig.BaselineModeFeatureOff);

			Assert.True(defaultBaseline.First(f => f.FeatureId == "control").Enabled);
			Assert.False(defaultBaseline.First(f => f.FeatureId == "warUnlock").Enabled);
			Assert.Equal(
				defaultBaseline.Select(f => (f.FeatureId, f.Enabled)).ToList(),
				explicitFeatureOff.Select(f => (f.FeatureId, f.Enabled)).ToList());
		}

		[Fact]
		void control_only_without_enabled_control_is_config_error() {
			var withoutControl = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "warUnlock", Enabled = true }
			};
			var disabledControl = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "control", Enabled = false },
				new BotFeatureConfigEntry { FeatureId = "warUnlock", Enabled = true }
			};
			var withControl = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "control", Enabled = true },
				new BotFeatureConfigEntry { FeatureId = "warUnlock", Enabled = true }
			};

			Assert.NotNull(EvalConfig.ValidateBaselineMode(EvalConfig.BaselineModeControlOnly, withoutControl));
			Assert.NotNull(EvalConfig.ValidateBaselineMode(EvalConfig.BaselineModeControlOnly, disabledControl));
			Assert.Null(EvalConfig.ValidateBaselineMode(EvalConfig.BaselineModeControlOnly, withControl));
			Assert.Null(EvalConfig.ValidateBaselineMode(EvalConfig.BaselineModeFeatureOff, withoutControl));
		}

		[Fact]
		void omitted_baseline_mode_and_score_gate_default_to_legacy_values() {
			var config = EvalConfig.Default();
			Assert.Equal(EvalConfig.BaselineModeFeatureOff, config.BaselineMode);
			Assert.Equal(EvalConfig.ScoreGateNonRegression, config.ScoreGate);
		}
	}
}
