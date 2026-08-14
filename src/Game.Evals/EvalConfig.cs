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
		public const string BaselineModeFeatureOff = "featureOff";
		public const string BaselineModeControlOnly = "controlOnly";
		public const string ScoreGateNonRegression = "nonRegression";
		public const string ScoreGateImprove = "improve";

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
		public string BaselineMode { get; set; } = BaselineModeFeatureOff;
		public string ScoreGate { get; set; } = ScoreGateNonRegression;

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

		// Returns a config-error message, or null when baselineMode is usable with the candidate list.
		public static string? ValidateBaselineMode(string baselineMode, IReadOnlyList<BotFeatureConfigEntry> candidateFeatures) {
			if (baselineMode == BaselineModeFeatureOff) { return null; }
			if (baselineMode == BaselineModeControlOnly) {
				foreach (var entry in candidateFeatures) {
					if (entry.FeatureId == "control" && entry.Enabled) { return null; }
				}
				return "baselineMode 'controlOnly' requires candidateFeatures to include enabled 'control'.";
			}
			return $"Unknown baselineMode '{baselineMode}' (expected '{BaselineModeFeatureOff}' or '{BaselineModeControlOnly}').";
		}

		public static string? ValidateScoreGate(string scoreGate) {
			if (scoreGate == ScoreGateNonRegression || scoreGate == ScoreGateImprove) { return null; }
			return $"Unknown scoreGate '{scoreGate}' (expected '{ScoreGateNonRegression}' or '{ScoreGateImprove}').";
		}

		// featureOff: byte-identical to candidate except <featureId> enabled=false.
		// controlOnly: keep control enabled; disable every other feature entry.
		public static List<BotFeatureConfigEntry> BuildBaselineFeatures(
			List<BotFeatureConfigEntry> candidateFeatures,
			string featureId,
			string baselineMode = BaselineModeFeatureOff
		) {
			if (baselineMode == BaselineModeControlOnly) {
				return BuildControlOnlyBaseline(candidateFeatures);
			}
			if (baselineMode != BaselineModeFeatureOff) {
				throw new InvalidOperationException(
					$"Unknown baselineMode '{baselineMode}' (expected '{BaselineModeFeatureOff}' or '{BaselineModeControlOnly}').");
			}
			var result = new List<BotFeatureConfigEntry>();
			foreach (var entry in candidateFeatures) {
				var clone = entry.Clone();
				if (clone.FeatureId == featureId) { clone.Enabled = false; }
				result.Add(clone);
			}
			return result;
		}

		public static List<BotFeatureConfigEntry> BuildControlOnlyBaseline(List<BotFeatureConfigEntry> candidateFeatures) {
			var result = new List<BotFeatureConfigEntry>();
			foreach (var entry in candidateFeatures) {
				var clone = entry.Clone();
				clone.Enabled = clone.FeatureId == "control";
				result.Add(clone);
			}
			return result;
		}
	}
}
