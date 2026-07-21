using GS.Game.Benchmarks;
using Xunit;

namespace GS.Game.Tests {
	public class BenchmarkGateTests {
		[Fact]
		void mean_at_or_below_epsilon_threshold_passes() {
			// baseline 100ns, epsilon 5% -> threshold is 105ns inclusive.
			Assert.True(BenchmarkGate.Passes(105.0, 100.0, 0.05));
			Assert.True(BenchmarkGate.Passes(100.0, 100.0, 0.05));
		}

		[Fact]
		void mean_above_epsilon_threshold_fails() {
			Assert.False(BenchmarkGate.Passes(105.01, 100.0, 0.05));
		}

		[Fact]
		void benchmark_with_no_baseline_entry_auto_passes_as_new() {
			Assert.True(BenchmarkGate.Passes(1_000_000.0, null, 0.05));
		}

		[Fact]
		void custom_epsilon_overrides_default() {
			// A looser epsilon (20%) accepts a mean that a tighter one (5%) would reject.
			Assert.False(BenchmarkGate.Passes(115.0, 100.0, 0.05));
			Assert.True(BenchmarkGate.Passes(115.0, 100.0, 0.20));
		}
	}
}
