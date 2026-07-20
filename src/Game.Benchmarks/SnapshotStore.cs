using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GS.Game.Benchmarks {
	public static class SnapshotStore {
		static readonly JsonSerializerOptions s_writeOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};
		static readonly JsonSerializerOptions s_readOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true
		};

		public static string DefaultDir => Path.Combine("Docs", "Benchmarks");

		public static string BaselinePath(string? dir = null) => Path.Combine(dir ?? DefaultDir, "baseline.json");
		public static string HistoryPath(string? dir = null) => Path.Combine(dir ?? DefaultDir, "history.json");
		public static string SummaryPath(string? dir = null) => Path.Combine(dir ?? DefaultDir, "summary.md");

		public static Dictionary<string, BenchmarkEntry> LoadBaseline(string? dir = null) {
			string path = BaselinePath(dir);
			if (!File.Exists(path)) { return new Dictionary<string, BenchmarkEntry>(); }
			string json = File.ReadAllText(path);
			var list = JsonSerializer.Deserialize<List<BenchmarkEntry>>(json, s_readOptions) ?? new List<BenchmarkEntry>();
			var byName = new Dictionary<string, BenchmarkEntry>();
			foreach (var entry in list) { byName[entry.Name] = entry; }
			return byName;
		}

		// Merges the just-run results into the existing baseline by name - entries for
		// benchmarks not covered by this run (e.g. a --benchmark-filtered run) are preserved.
		public static void SaveBaseline(IReadOnlyList<BenchmarkEntry> results, string? dir = null) {
			var baseline = LoadBaseline(dir);
			foreach (var entry in results) { baseline[entry.Name] = entry; }

			var ordered = new List<BenchmarkEntry>(baseline.Values);
			ordered.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

			string path = BaselinePath(dir);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(ordered, s_writeOptions));
		}

		// Append-only: earlier entries are never rewritten or deleted.
		public static void AppendHistory(BenchmarkRunRecord record, string? dir = null) {
			var history = LoadHistory(dir);
			history.Add(record);
			string path = HistoryPath(dir);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(history, s_writeOptions));
		}

		public static List<BenchmarkRunRecord> LoadHistory(string? dir = null) {
			string path = HistoryPath(dir);
			if (!File.Exists(path)) { return new List<BenchmarkRunRecord>(); }
			string json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<List<BenchmarkRunRecord>>(json, s_readOptions) ?? new List<BenchmarkRunRecord>();
		}

		public static void RewriteSummary(BenchmarkRunRecord record, IReadOnlyDictionary<string, BenchmarkEntry> baselineBeforeUpdate, string? dir = null) {
			var sb = new StringBuilder();
			sb.AppendLine("# Benchmark Summary");
			sb.AppendLine();
			sb.AppendLine("> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. " +
				"Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable " +
				"to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container " +
				"class of machine) - no cross-machine normalization is attempted.");
			sb.AppendLine();
			sb.AppendLine($"Mode: `{record.Mode}`");
			sb.AppendLine($"Timestamp: {record.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
			sb.AppendLine($"Overall: {(record.Pass ? "PASS" : "FAIL")}");
			sb.AppendLine();
			sb.AppendLine("| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |");
			sb.AppendLine("|---|---|---|---|---|---|");
			foreach (var entry in record.Results) {
				string baselineCell;
				string changeCell;
				string verdictCell;
				if (baselineBeforeUpdate.TryGetValue(entry.Name, out var baselineEntry)) {
					baselineCell = baselineEntry.MeanNanoseconds.ToString("F1");
					double percentChange = baselineEntry.MeanNanoseconds > 0
						? (entry.MeanNanoseconds - baselineEntry.MeanNanoseconds) / baselineEntry.MeanNanoseconds * 100.0
						: 0.0;
					changeCell = $"{percentChange:+0.0;-0.0;0.0}%";
					verdictCell = record.Verdicts.TryGetValue(entry.Name, out bool pass) ? (pass ? "pass" : "FAIL") : "n/a";
				} else {
					baselineCell = "-";
					changeCell = "-";
					verdictCell = "new - no baseline";
				}
				sb.AppendLine($"| {entry.Name} | {baselineCell} | {entry.MeanNanoseconds:F1} | {changeCell} | {verdictCell} | {entry.AllocatedBytes} |");
			}

			string path = SummaryPath(dir);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, sb.ToString());
		}
	}
}
