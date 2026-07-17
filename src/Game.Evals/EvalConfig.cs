using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GS.Game.Evals {
	public class BotFeatureConfigEntry {
		public string FeatureId { get; set; } = "";
		public bool Enabled { get; set; } = true;
		public Dictionary<string, double> Parameters { get; set; } = new();

		public BotFeatureConfigEntry Clone() => new BotFeatureConfigEntry {
			FeatureId = FeatureId,
			Enabled = Enabled,
			Parameters = new Dictionary<string, double>(Parameters)
		};
	}

	public class ParameterRange {
		public List<double>? Values { get; set; }
		public double? Min { get; set; }
		public double? Max { get; set; }
		public double? Step { get; set; }

		// {min,max,step} expands to min, min+step, ... <= max (inclusive), rounded to avoid
		// floating-point drift accumulating across many additions.
		public List<double> Expand() {
			if (Values != null) { return new List<double>(Values); }
			if (Min.HasValue && Max.HasValue && Step.HasValue && Step.Value > 0) {
				var result = new List<double>();
				for (double v = Min.Value; v <= Max.Value + 1e-9; v += Step.Value) {
					result.Add(Math.Round(v, 6));
				}
				return result;
			}
			throw new InvalidOperationException("Parameter range must declare either 'values' or 'min'/'max'/'step'.");
		}
	}

	public class ParameterSearchConfig {
		public string Mode { get; set; } = "grid";
		public Dictionary<string, ParameterRange> Parameters { get; set; } = new();
		public int MaxCandidates { get; set; } = 20;
		public int SearchSeed { get; set; } = 7;
	}

	public class EvalConfig {
		public string? CandidateOrgId { get; set; }
		public List<BotFeatureConfigEntry> OpponentFeatures { get; set; } = new() {
			new BotFeatureConfigEntry { FeatureId = "baselineCardPlay", Enabled = true }
		};
		public List<BotFeatureConfigEntry>? CandidateFeatures { get; set; }
		public int SeedCount { get; set; } = 10;
		public int BaseSeed { get; set; } = 1880;
		public string? EndDate { get; set; }
		public int HoursPerTick { get; set; } = 24;
		public int TimeoutSeconds { get; set; } = 300;
		public double EpsilonRelative { get; set; } = 0.02;
		public double EpsilonAbsolute { get; set; } = 0;
		public int MaxTotalRuns { get; set; } = 200;
		public List<string> TargetActions { get; set; } = new();
		public ParameterSearchConfig? ParameterSearch { get; set; }

		public static EvalConfig Default() => new EvalConfig();

		public static EvalConfig Load(string path) {
			if (!File.Exists(path)) {
				throw new FileNotFoundException($"Eval config not found: '{path}'.", path);
			}
			string json = File.ReadAllText(path);
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			return JsonSerializer.Deserialize<EvalConfig>(json, options)
				?? throw new InvalidOperationException($"Failed to deserialize eval config from '{path}'.");
		}

		// candidateFeatures default: baselineCardPlay + <featureId>, deduplicated when
		// <featureId> IS baselineCardPlay.
		public List<BotFeatureConfigEntry> ResolveCandidateFeatures(string featureId) {
			if (CandidateFeatures != null) { return CandidateFeatures; }
			var result = new List<BotFeatureConfigEntry> {
				new BotFeatureConfigEntry { FeatureId = "baselineCardPlay", Enabled = true }
			};
			if (featureId != "baselineCardPlay") {
				result.Add(new BotFeatureConfigEntry { FeatureId = featureId, Enabled = true });
			}
			return result;
		}

		// The baseline arm is byte-identical to the candidate arm except <featureId>'s
		// enabled flag flipped to false.
		public static List<BotFeatureConfigEntry> BuildBaselineFeatures(List<BotFeatureConfigEntry> candidateFeatures, string featureId) {
			var result = new List<BotFeatureConfigEntry>();
			foreach (var entry in candidateFeatures) {
				var clone = entry.Clone();
				if (clone.FeatureId == featureId) { clone.Enabled = false; }
				result.Add(clone);
			}
			return result;
		}
	}
}
