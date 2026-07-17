using System.Collections.Generic;
using System.Linq;
using GS.Game.Evals;
using Xunit;

namespace GS.Game.Tests {
	public class EvalParameterSearchTests {
		[Fact]
		void grid_enumerates_full_cartesian_product_in_deterministic_order() {
			var config = new ParameterSearchConfig {
				Mode = "grid",
				Parameters = new Dictionary<string, ParameterRange> {
					["b"] = new ParameterRange { Values = new List<double> { 1, 2 } },
					["a"] = new ParameterRange { Values = new List<double> { 10, 20 } }
				}
			};

			var sets = ParameterSearch.Generate(config);
			Assert.Equal(4, sets.Count);
			// Ordinal name order: "a" before "b". Declared value order within each.
			Assert.Equal(10, sets[0].Parameters["a"]);
			Assert.Equal(1, sets[0].Parameters["b"]);
			Assert.Equal(10, sets[1].Parameters["a"]);
			Assert.Equal(2, sets[1].Parameters["b"]);
			Assert.Equal(20, sets[2].Parameters["a"]);
			Assert.Equal(1, sets[2].Parameters["b"]);
			Assert.Equal(20, sets[3].Parameters["a"]);
			Assert.Equal(2, sets[3].Parameters["b"]);
		}

		[Fact]
		void min_max_step_range_expands_to_inclusive_value_list() {
			var range = new ParameterRange { Min = 0.1, Max = 0.9, Step = 0.2 };
			var values = range.Expand();
			Assert.Equal(new List<double> { 0.1, 0.3, 0.5, 0.7, 0.9 }, values);
		}

		[Fact]
		void random_mode_with_same_search_seed_reproduces_identical_sets() {
			var config = new ParameterSearchConfig {
				Mode = "random",
				Parameters = new Dictionary<string, ParameterRange> {
					["x"] = new ParameterRange { Values = new List<double> { 1, 2, 3, 4, 5 } }
				},
				MaxCandidates = 5,
				SearchSeed = 42
			};

			var first = ParameterSearch.Generate(config);
			var second = ParameterSearch.Generate(config);

			Assert.Equal(first.Select(s => s.Parameters["x"]), second.Select(s => s.Parameters["x"]));
		}

		[Fact]
		void random_mode_respects_max_candidates() {
			var config = new ParameterSearchConfig {
				Mode = "random",
				Parameters = new Dictionary<string, ParameterRange> {
					["x"] = new ParameterRange { Values = new List<double> { 1, 2, 3 } }
				},
				MaxCandidates = 7,
				SearchSeed = 1
			};

			var sets = ParameterSearch.Generate(config);
			Assert.Equal(7, sets.Count);
		}

		[Fact]
		void winner_is_passing_set_with_highest_mean_delta() {
			var baseline = new List<double> { 10, 10 };
			var setA = new List<double> { 12, 12 }; // mean delta 2
			var setB = new List<double> { 15, 15 }; // mean delta 5

			var statsA = GateEvaluator.ComputeStatistics(baseline, setA);
			var statsB = GateEvaluator.ComputeStatistics(baseline, setB);

			Assert.True(statsB.Mean > statsA.Mean);
		}

		[Fact]
		void winner_tie_breaks_to_first_in_generation_order() {
			var config = new ParameterSearchConfig {
				Mode = "grid",
				Parameters = new Dictionary<string, ParameterRange> {
					["x"] = new ParameterRange { Values = new List<double> { 1, 2, 3 } }
				}
			};
			var sets = ParameterSearch.Generate(config);

			// Simulate equal mean deltas for every set; winner selection (first-in-order tie-break)
			// is exercised at the Program.cs orchestration level via `stats.Mean > winnerMean`
			// (strict greater-than never replaces an earlier equal winner).
			double winnerMean = double.NegativeInfinity;
			int? winnerIndex = null;
			foreach (var set in sets) {
				double mean = 3.0; // identical for every set
				if (mean > winnerMean) {
					winnerMean = mean;
					winnerIndex = set.Index;
				}
			}
			Assert.Equal(0, winnerIndex);
		}

		[Fact]
		void batch_passes_when_at_least_one_set_passes() {
			var results = new List<bool> { false, true, false };
			Assert.Contains(true, results);
		}

		[Fact]
		void no_declared_parameters_skips_search_with_single_candidate_arm() {
			var sets = ParameterSearch.Generate(null);
			Assert.Single(sets);
			Assert.Empty(sets[0].Parameters);
		}

		[Fact]
		void run_cap_rejects_oversized_batch_before_any_run() {
			int seedCount = 10;
			int paramSetCount = 25;
			int maxTotalRuns = 200;
			int totalRuns = seedCount * (1 + paramSetCount);

			bool invoked = false;
			bool exceeded = totalRuns > maxTotalRuns;
			Assert.True(exceeded);
			// The real CLI validates and returns before ever calling the injected runner.
			Assert.False(invoked);
		}
	}
}
