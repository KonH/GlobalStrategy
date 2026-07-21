namespace GS.Game.Benchmarks {
	public static class BenchmarkGate {
		public const double DefaultEpsilonRelative = 0.05;

		// A benchmark with no baseline entry always passes ("new - no baseline").
		public static bool Passes(double meanCurrentNanoseconds, double? meanBaselineNanoseconds, double epsilonRelative) {
			if (meanBaselineNanoseconds == null) { return true; }
			return meanCurrentNanoseconds <= meanBaselineNanoseconds.Value * (1 + epsilonRelative);
		}
	}
}
