using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GS.Game.Configs;

namespace GS.Game.ConsoleRunner.WarSim {
	// Entry point for `dotnet run --project src/Game.ConsoleRunner -- war-scenarios`.
	// Runs the 7 baseline war scenarios (WarScenarioSpec.All) against current production war-balance
	// settings, several rng seeds each, and writes an aggregated markdown table to .tmp/battle_logic.md.
	public static class WarScenarioCli {
		const int SeedsPerScenario = 3;
		const int MaxDays = 3650; // 10 in-game years safety cap before a scenario is reported "Unresolved"

		public static int Run(string[] args) {
			string outputPath = ".tmp/battle_logic.md";
			for (int i = 1; i < args.Length; i++) {
				if (args[i] == "--output" && i + 1 < args.Length) {
					outputPath = args[++i];
				}
			}

			List<AggregatedResult> rows = RunScenarios(null);
			ApplyBaselineToFile(outputPath, rows);
			Console.WriteLine($"Wrote {outputPath}");
			return 0;
		}

		// Splices the baseline description+table into an existing hand-authored doc (Goal/Ideas
		// checklist sections stay untouched) between the "## Ideas" block and the first "### Idea "
		// heading — same in-place-merge shape as WarIdeaCli.ApplyToBattleLogic. Falls back to writing
		// a fresh minimal doc (just the heading + baseline section) if the file doesn't exist yet or
		// has no recognizable baseline marker.
		public static void ApplyBaselineToFile(string path, List<AggregatedResult> rows) {
			string baselineSection = BuildBaselineSection(rows);
			string content = File.Exists(path) ? File.ReadAllText(path) : "";
			const string marker = "Headless simulation via `src/Game.ConsoleRunner/WarSim`";
			int start = content.IndexOf(marker, StringComparison.Ordinal);
			if (start < 0) {
				content = "# War Battle Logic — Baseline Simulation\n\n" + baselineSection.TrimEnd() + "\n";
			} else {
				int end = content.IndexOf("\n### Idea ", start, StringComparison.Ordinal);
				if (end < 0) {
					end = content.Length;
				}
				content = content[..start] + baselineSection.TrimEnd() + "\n" + content[end..];
			}

			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) {
				Directory.CreateDirectory(dir);
			}
			File.WriteAllText(path, content);
		}

		// Runs all 7 scenarios x SeedsPerScenario seeds against the baseline settings, optionally
		// with a settings override applied (used by idea A-F comparison runs) — same code path as
		// the baseline `war-scenarios` verb so results stay directly comparable.
		public static List<AggregatedResult> RunScenarios(Action<GameSettings>? configureSettings) {
			var rows = new List<AggregatedResult>();
			foreach (WarScenarioSpec spec in WarScenarioSpec.All()) {
				Console.WriteLine($"Running: {spec.Name}");
				var runs = new List<WarScenarioResult>();
				for (int s = 0; s < SeedsPerScenario; s++) {
					int seed = 1000 * (s + 1) + spec.Name.Length;
					WarScenarioResult result = WarScenarioRunner.RunOnce(spec, seed, MaxDays, configureSettings);
					runs.Add(result);
					Console.WriteLine(
						$"  seed {seed}: {(result.Resolved ? "resolved" : "UNRESOLVED")} in {result.DurationDays}d, " +
						$"{result.BattleCount} battles, winner={result.Winner}, " +
						$"losses A={result.AttackerLossPercent:N1}% D={result.DefenderLossPercent:N1}%");
				}
				rows.Add(Aggregate(spec.Name, runs));
			}
			return rows;
		}

		public class AggregatedResult {
			public string Name = "";
			public double AvgDurationDays;
			public double AvgBattleCount;
			public double AvgLoserProvincesOccupiedPercent;
			public double AvgAttackerLossPercent;
			public double AvgDefenderLossPercent;
			public string WinnerSummary = "";
			// % of resolved runs (with a determined first-battle winner) where that side also won the
			// war — the "one grand battle decides everything" indicator. NaN if no run qualifies.
			public double FirstBattleDecidesWarPercent;
		}

		static AggregatedResult Aggregate(string name, List<WarScenarioResult> runs) {
			int n = runs.Count;
			int attackerWins = runs.Count(r => r.Winner == "Attacker");
			int defenderWins = runs.Count(r => r.Winner == "Defender");
			int unresolved = runs.Count(r => r.Winner == "Unresolved");
			string winnerSummary = unresolved == n
				? "Unresolved (all runs hit cap)"
				: attackerWins >= defenderWins
					? $"Attacker ({attackerWins}/{n})" + (unresolved > 0 ? $", {unresolved} unresolved" : "")
					: $"Defender ({defenderWins}/{n})" + (unresolved > 0 ? $", {unresolved} unresolved" : "");

			List<bool> firstBattleDecided = runs
				.Where(r => r.FirstBattleWinnerWonWar.HasValue)
				.Select(r => r.FirstBattleWinnerWonWar!.Value)
				.ToList();

			return new AggregatedResult {
				Name = name,
				AvgDurationDays = runs.Average(r => (double)r.DurationDays),
				AvgBattleCount = runs.Average(r => (double)r.BattleCount),
				AvgLoserProvincesOccupiedPercent = runs.Average(r => r.LoserProvincesOccupiedPercent),
				AvgAttackerLossPercent = runs.Average(r => r.AttackerLossPercent),
				AvgDefenderLossPercent = runs.Average(r => r.DefenderLossPercent),
				WinnerSummary = winnerSummary,
				FirstBattleDecidesWarPercent = firstBattleDecided.Count == 0
					? double.NaN
					: 100.0 * firstBattleDecided.Count(x => x) / firstBattleDecided.Count
			};
		}

		static string BuildBaselineSection(List<AggregatedResult> rows) {
			var sb = new StringBuilder();
			sb.AppendLine(
				$"Headless simulation via `src/Game.ConsoleRunner/WarSim`, current production " +
				$"`Assets/Configs/game_settings.json` war-balance values, {SeedsPerScenario} rng seeds " +
				$"per scenario (averaged below), {MaxDays}-day (10 in-game year) timeout cap. " +
				$"Attacker role = France's real 65-province graph, defender role = Germany's real " +
				$"50-province graph (`Assets/Configs/province_config.json`, real shared border); only " +
				$"each side's total population/damage/durability are overridden per scenario, spread " +
				$"evenly across its real provinces.");
			sb.AppendLine();
			sb.AppendLine(
				"| Case | War duration | Battle count | Occupied provinces (% of loser's provinces, peak) | Attacker losses (% of initial recruits) | Defender losses (% of initial recruits) | First-battle winner = war winner | Winner |");
			sb.AppendLine(
				"|---|---|---|---|---|---|---|---|");
			foreach (AggregatedResult row in rows) {
				sb.AppendLine(
					$"| {row.Name} | {FormatDuration(row.AvgDurationDays)} | {row.AvgBattleCount:N1} | " +
					$"{row.AvgLoserProvincesOccupiedPercent:N1}% | {row.AvgAttackerLossPercent:N1}% | {row.AvgDefenderLossPercent:N1}% | " +
					$"{FormatFirstBattlePercent(row.FirstBattleDecidesWarPercent)} | {row.WinnerSummary} |");
			}
			return sb.ToString();
		}

		static string FormatFirstBattlePercent(double percent) {
			return double.IsNaN(percent) ? "n/a" : $"{percent:N1}%";
		}

		static string FormatDuration(double days) {
			int totalDays = (int)Math.Round(days, MidpointRounding.AwayFromZero);
			int years = totalDays / 365;
			int months = (totalDays % 365) / 30;
			int remDays = (totalDays % 365) % 30;
			var parts = new List<string>();
			if (years > 0) {
				parts.Add($"{years}y");
			}
			if (months > 0) {
				parts.Add($"{months}mo");
			}
			if (remDays > 0 || parts.Count == 0) {
				parts.Add($"{remDays}d");
			}
			return string.Join(" ", parts) + $" ({totalDays.ToString(CultureInfo.InvariantCulture)}d)";
		}
	}
}
