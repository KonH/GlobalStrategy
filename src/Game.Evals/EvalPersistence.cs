using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GS.Game.Evals {
	public class PerSeedRecord {
		public int Seed { get; set; }
		public double BaselineScore { get; set; }
		public double CandidateScore { get; set; }
		public double Delta { get; set; }
	}

	public class StatsRecord {
		public double Mean { get; set; }
		public double Median { get; set; }
		public double Min { get; set; }
		public double Max { get; set; }
		public double StdDev { get; set; }
		public int Improved { get; set; }
		public int Worsened { get; set; }
		public int Unchanged { get; set; }
	}

	public class ParameterSetRecord {
		public int Index { get; set; }
		public Dictionary<string, double> Parameters { get; set; } = new();
		public bool ScoreGatePass { get; set; }
		public bool CommandOnPass { get; set; }
		public StatsRecord Stats { get; set; } = new();
		public List<PerSeedRecord> PerSeed { get; set; } = new();
	}

	public class VerdictRecord {
		public bool Pass { get; set; }
		public bool ScoreGate { get; set; }
		public bool CommandOn { get; set; }
		public bool CommandOff { get; set; }
	}

	public class AttemptRecord {
		public int Attempt { get; set; }
		public string Date { get; set; } = "";
		public VerdictRecord Verdict { get; set; } = new();
		public bool Improved { get; set; }
		public double Epsilon { get; set; }
		public EvalConfig EffectiveConfig { get; set; } = new();
		public List<ParameterSetRecord> ParameterSets { get; set; } = new();
		public int? Winner { get; set; }
		public string RawRunDir { get; set; } = "";
	}

	public static class EvalPersistence {
		static readonly JsonSerializerOptions s_writeOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};
		static readonly JsonSerializerOptions s_readOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true
		};

		public static string HistoryPath(string featureId) => Path.Combine("Docs", "BotFeatures", featureId, "eval_history.json");
		public static string SummaryPath(string featureId) => Path.Combine("Docs", "BotFeatures", featureId, "eval_summary.md");

		public static List<AttemptRecord> LoadHistory(string featureId) {
			string path = HistoryPath(featureId);
			if (!File.Exists(path)) { return new List<AttemptRecord>(); }
			string json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<List<AttemptRecord>>(json, s_readOptions) ?? new List<AttemptRecord>();
		}

		public static int NextAttemptNumber(string featureId) {
			var history = LoadHistory(featureId);
			int max = 0;
			foreach (var record in history) {
				if (record.Attempt > max) { max = record.Attempt; }
			}
			return max + 1;
		}

		// Append-only: earlier entries are never rewritten or deleted.
		public static void AppendHistory(string featureId, AttemptRecord record) {
			var history = LoadHistory(featureId);
			history.Add(record);
			string path = HistoryPath(featureId);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(history, s_writeOptions));
		}

		public static void WriteSummary(string featureId, AttemptRecord latest, int attemptCount) {
			var sb = new StringBuilder();
			sb.AppendLine($"# Eval Summary: {featureId}");
			sb.AppendLine();
			sb.AppendLine($"Attempts: {attemptCount}");
			sb.AppendLine($"Latest attempt: {latest.Attempt} ({latest.Date})");
			sb.AppendLine();
			sb.AppendLine("## Verdict");
			sb.AppendLine($"- Pass: {latest.Verdict.Pass}");
			sb.AppendLine($"- Score gate: {latest.Verdict.ScoreGate}");
			sb.AppendLine($"- Command on: {latest.Verdict.CommandOn}");
			sb.AppendLine($"- Command off: {latest.Verdict.CommandOff}");
			sb.AppendLine($"- Improved: {latest.Improved}");
			sb.AppendLine($"- Epsilon: {latest.Epsilon}");
			sb.AppendLine();
			sb.AppendLine("## Parameter sets");
			sb.AppendLine();
			sb.AppendLine("| Index | Parameters | Score gate | Command on | Mean delta | Median | Min | Max | StdDev |");
			sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
			foreach (var set in latest.ParameterSets) {
				var parameterParts = new List<string>();
				foreach (var kv in set.Parameters) { parameterParts.Add($"{kv.Key}={kv.Value}"); }
				string parameters = string.Join(", ", parameterParts);
				sb.AppendLine($"| {set.Index} | {parameters} | {set.ScoreGatePass} | {set.CommandOnPass} | {set.Stats.Mean:F3} | {set.Stats.Median:F3} | {set.Stats.Min:F3} | {set.Stats.Max:F3} | {set.Stats.StdDev:F3} |");
			}
			sb.AppendLine();
			sb.AppendLine($"Winner: {(latest.Winner.HasValue ? latest.Winner.Value.ToString() : "none")}");
			sb.AppendLine();
			sb.AppendLine($"Raw run dir: `{latest.RawRunDir}`");

			string path = SummaryPath(featureId);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, sb.ToString());
		}
	}
}
