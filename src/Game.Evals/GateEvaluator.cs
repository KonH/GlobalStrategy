using System;
using System.Collections.Generic;

namespace GS.Game.Evals {
	public class DeltaStatistics {
		public double Mean { get; init; }
		public double Median { get; init; }
		public double Min { get; init; }
		public double Max { get; init; }
		public double StdDev { get; init; }
		public int Improved { get; init; }
		public int Worsened { get; init; }
		public int Unchanged { get; init; }
		public IReadOnlyList<double> Deltas { get; init; } = Array.Empty<double>();
	}

	public static class GateEvaluator {
		// epsilonAbsolute is an explicit floor via Math.Max — covers mean(baseline) == 0
		// (scores are non-negative by construction) without a special case.
		public static double ComputeEpsilon(IReadOnlyList<double> baselineScores, double epsilonRelative, double epsilonAbsolute) {
			double meanBaseline = Mean(baselineScores);
			return Math.Max(epsilonRelative * meanBaseline, epsilonAbsolute);
		}

		public static DeltaStatistics ComputeStatistics(IReadOnlyList<double> baselineScores, IReadOnlyList<double> candidateScores) {
			int n = baselineScores.Count;
			var deltas = new double[n];
			int improved = 0, worsened = 0, unchanged = 0;
			for (int i = 0; i < n; i++) {
				deltas[i] = candidateScores[i] - baselineScores[i];
				if (deltas[i] > 0) { improved++; } else if (deltas[i] < 0) { worsened++; } else { unchanged++; }
			}

			double mean = Mean(deltas);

			var sorted = (double[])deltas.Clone();
			Array.Sort(sorted);
			double median = sorted.Length == 0 ? 0
				: sorted.Length % 2 == 1
					? sorted[sorted.Length / 2]
					: (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
			double min = sorted.Length > 0 ? sorted[0] : 0;
			double max = sorted.Length > 0 ? sorted[^1] : 0;

			double variance = 0;
			foreach (var d in deltas) { variance += (d - mean) * (d - mean); }
			variance = n > 1 ? variance / (n - 1) : 0;
			double stdDev = Math.Sqrt(variance);

			return new DeltaStatistics {
				Mean = mean,
				Median = median,
				Min = min,
				Max = max,
				StdDev = stdDev,
				Improved = improved,
				Worsened = worsened,
				Unchanged = unchanged,
				Deltas = deltas
			};
		}

		// Inclusive boundary: mean(d) == -epsilon passes.
		public static bool ScoreGatePasses(double meanDelta, double epsilon) => meanDelta >= -epsilon;

		public static bool ImprovedFlag(double meanDelta) => meanDelta > 0;

		static double Mean(IReadOnlyList<double> values) {
			if (values.Count == 0) { return 0; }
			double sum = 0;
			foreach (var v in values) { sum += v; }
			return sum / values.Count;
		}
	}
}
