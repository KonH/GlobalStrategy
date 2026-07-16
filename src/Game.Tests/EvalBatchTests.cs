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
	}
}
