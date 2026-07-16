using System;
using System.Collections.Generic;

namespace GS.Game.Evals {
	public static class BatchRunner {
		public class RunRequest {
			public int Seed { get; init; }
			public string Arm { get; init; } = "";
			public int ParameterSetIndex { get; init; } = -1;
			public IReadOnlyDictionary<string, double> Parameters { get; init; } = new Dictionary<string, double>();
		}

		public class RunOutcome {
			public bool Success { get; init; } = true;
			public double Score { get; init; }
			public IReadOnlyList<(string FeatureId, string ActionId)> Emissions { get; init; } = Array.Empty<(string, string)>();
			public string? Error { get; init; }
		}

		public class ParameterSetOutcome {
			public ParameterSet Set { get; init; } = null!;
			public IReadOnlyList<RunOutcome> CandidateRuns { get; init; } = Array.Empty<RunOutcome>();
		}

		public class BatchResult {
			public IReadOnlyList<RunOutcome> BaselineRuns { get; init; } = Array.Empty<RunOutcome>();
			public IReadOnlyList<ParameterSetOutcome> ParameterSets { get; init; } = Array.Empty<ParameterSetOutcome>();
		}

		public class BatchFailure : Exception {
			public BatchFailure(string message) : base(message) { }
		}

		// Baseline seeds ascending, then parameter sets in generation order x seeds ascending.
		// Baseline runs once per batch (feature-off is parameter-independent) and is shared
		// across every parameter set's comparison.
		public static BatchResult Run(
			IReadOnlyList<int> seeds,
			IReadOnlyList<ParameterSet> parameterSets,
			Func<RunRequest, RunOutcome> runFn) {
			var baselineRuns = new List<RunOutcome>();
			foreach (int seed in seeds) {
				var outcome = runFn(new RunRequest { Seed = seed, Arm = "baseline", ParameterSetIndex = -1, Parameters = new Dictionary<string, double>() });
				if (!outcome.Success) {
					throw new BatchFailure($"Run failed: seed={seed} arm=baseline: {outcome.Error}");
				}
				baselineRuns.Add(outcome);
			}

			var parameterSetOutcomes = new List<ParameterSetOutcome>();
			foreach (var set in parameterSets) {
				var candidateRuns = new List<RunOutcome>();
				foreach (int seed in seeds) {
					var outcome = runFn(new RunRequest { Seed = seed, Arm = "candidate", ParameterSetIndex = set.Index, Parameters = set.Parameters });
					if (!outcome.Success) {
						throw new BatchFailure($"Run failed: seed={seed} arm=candidate parameterSet={set.Index}: {outcome.Error}");
					}
					candidateRuns.Add(outcome);
				}
				parameterSetOutcomes.Add(new ParameterSetOutcome { Set = set, CandidateRuns = candidateRuns });
			}

			return new BatchResult { BaselineRuns = baselineRuns, ParameterSets = parameterSetOutcomes };
		}
	}
}
