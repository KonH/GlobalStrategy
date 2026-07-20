using System;
using System.Collections.Generic;
using System.IO;
using GS.Game.Benchmarks;
using Xunit;

namespace GS.Game.Tests {
	// Uses a unique temp directory per test (never the real Docs/Benchmarks/) so these tests
	// can never overwrite the committed baseline/history.
	public class BenchmarkSnapshotStoreTests : IDisposable {
		readonly string _dir = Path.Combine(Path.GetTempPath(), "gs_benchmark_snapshot_tests_" + Guid.NewGuid().ToString("N"));

		public void Dispose() {
			if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); }
		}

		[Fact]
		void update_baseline_with_filter_only_overwrites_filtered_entries() {
			SnapshotStore.SaveBaseline(new List<BenchmarkEntry> {
				new BenchmarkEntry { Name = "A.Method", MeanNanoseconds = 100 },
				new BenchmarkEntry { Name = "B.Method", MeanNanoseconds = 200 }
			}, _dir);

			// A --benchmark-filtered run only re-runs A - the update must leave B untouched.
			SnapshotStore.SaveBaseline(new List<BenchmarkEntry> {
				new BenchmarkEntry { Name = "A.Method", MeanNanoseconds = 150 }
			}, _dir);

			var baseline = SnapshotStore.LoadBaseline(_dir);
			Assert.Equal(150, baseline["A.Method"].MeanNanoseconds);
			Assert.Equal(200, baseline["B.Method"].MeanNanoseconds);
		}

		[Fact]
		void history_append_never_rewrites_earlier_entries() {
			var first = new BenchmarkRunRecord { Timestamp = new DateTime(2026, 1, 1), Mode = "compare", Pass = true };
			SnapshotStore.AppendHistory(first, _dir);

			var second = new BenchmarkRunRecord { Timestamp = new DateTime(2026, 1, 2), Mode = "compare", Pass = false };
			SnapshotStore.AppendHistory(second, _dir);

			var history = SnapshotStore.LoadHistory(_dir);
			Assert.Equal(2, history.Count);
			Assert.Equal(new DateTime(2026, 1, 1), history[0].Timestamp);
			Assert.True(history[0].Pass);
			Assert.Equal(new DateTime(2026, 1, 2), history[1].Timestamp);
			Assert.False(history[1].Pass);
		}

		[Fact]
		void summary_rewrite_includes_comparability_caveat_header() {
			var record = new BenchmarkRunRecord {
				Timestamp = DateTime.UtcNow,
				Mode = "compare",
				Pass = true,
				Results = new List<BenchmarkEntry> { new BenchmarkEntry { Name = "A.Method", MeanNanoseconds = 100 } }
			};

			SnapshotStore.RewriteSummary(record, new Dictionary<string, BenchmarkEntry>(), _dir);

			string summary = File.ReadAllText(SnapshotStore.SummaryPath(_dir));
			Assert.Contains("Comparability caveat", summary);
			Assert.Contains("machine- and environment-dependent", summary);
		}
	}
}
