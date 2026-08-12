using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GS.Game.Configs;

namespace GS.Game.ConsoleRunner.WarSim {
	// Entry point for `dotnet run --project src/Game.ConsoleRunner -- war-idea <letter>`.
	// Runs the same 7 scenarios/seeds as `war-scenarios`, once against baseline settings and once
	// with a single idea (A-F, see .tmp/battle_logic.md "Ideas") applied, then appends a
	// baseline -> new (delta) comparison table under that idea's section in .tmp/battle_logic.md.
	public static class WarIdeaCli {
		class Idea {
			public string Letter = "";
			public string Title = "";
			public string ChangeDescription = "";
			public Action<GameSettings> Configure = _ => { };
		}

		static readonly Dictionary<string, Idea> Ideas = new() {
			["A"] = new Idea {
				Letter = "A",
				Title = "Decrease battle -> war progress change",
				ChangeDescription = "`warBattles.battleProgressGain`: 5 -> 2 (-60%) — each battle round moves " +
					"war_progress less, so more battle rounds (and more days) are needed to reach the " +
					"+-100 threshold that ends the war.",
				Configure = s => s.WarBattles.BattleProgressGain = 2
			},
			["B"] = new Idea {
				Letter = "B",
				Title = "Increase interval between battles",
				ChangeDescription = "`warBattles.roundIntervalHours`: 8 -> 12 (+50%) — two battle rounds per " +
					"front per day instead of up to three, so the same war-progress swing (already slowed " +
					"by idea A) is spread over more calendar days without changing per-round outcomes.",
				Configure = s => s.WarBattles.RoundIntervalHours = 12
			},
			["C"] = new Idea {
				Letter = "C",
				Title = "Increase battle step count via reducing casualties via an additional coefficient",
				ChangeDescription = "`warBattles.casualtyCoefficient`: 1.0 -> 0.5 (-50%) — each round's computed " +
					"casualties are halved before the per-round minimum floor is applied, so a single battle " +
					"needs roughly twice as many rounds to fully wipe out a force; a war that resolves by " +
					"peace chance before that happens ends with fewer total casualties.",
				Configure = s => s.WarBattles.CasualtyCoefficient = 0.5
			},
			["D"] = new Idea {
				Letter = "D",
				Title = "Increase reserves (% of recruits not included in battles)",
				ChangeDescription = "`warBattles.reservePercent`: 0 -> 30 (+30pp) — 30% of a country's current " +
					"recruits are held back as a standing reserve every time forces are allocated to a new " +
					"battle, so committed (and thus at-risk) troops per battle shrink accordingly.",
				Configure = s => s.WarBattles.ReservePercent = 30
			},
			["E"] = new Idea {
				Letter = "E",
				Title = "Increase concurrent battle count",
				ChangeDescription = "`warBattles.baseConcurrentBattleCount`: 1 -> 2 (+100%) — France and " +
					"Germany share a real border (3 cross-border neighbor pairs), but `sharedPairs / " +
					"sharedBorderPairsPerAdditionalBattle` = 3/5 = 0 (integer division), so the base term " +
					"is still the only lever that raises capacity for this harness.",
				Configure = s => s.WarBattles.BaseConcurrentBattleCount = 2
			},
			["G"] = new Idea {
				Letter = "G",
				Title = "Widen casualty randomization range",
				ChangeDescription = "`warBattles.casualtyRandomMin`/`casualtyRandomMax`: 0.9-1.1 (range 0.2) " +
					"-> 0.8-1.2 (range 0.4) — doubles the per-round random swing already applied to " +
					"casualties, still centered on 1.0, using the existing fields rather than a new setting.",
				Configure = s => {
					s.WarBattles.CasualtyRandomMin = 0.8;
					s.WarBattles.CasualtyRandomMax = 1.2;
				}
			}
		};

		public static int Run(string[] args) {
			if (args.Length < 2 || !Ideas.TryGetValue(args[1].ToUpperInvariant(), out Idea? idea)) {
				Console.Error.WriteLine($"Usage: war-idea <letter>. Known ideas: {string.Join(", ", Ideas.Keys)}");
				return 1;
			}

			string outputPath = ".tmp/battle_logic.md";
			for (int i = 2; i < args.Length; i++) {
				if (args[i] == "--output" && i + 1 < args.Length) {
					outputPath = args[++i];
				}
			}

			Console.WriteLine($"--- Baseline run (idea {idea.Letter} comparison) ---");
			List<WarScenarioCli.AggregatedResult> baseline = WarScenarioCli.RunScenarios(null);

			Console.WriteLine($"--- Idea {idea.Letter} run: {idea.Title} ---");
			List<WarScenarioCli.AggregatedResult> variant = WarScenarioCli.RunScenarios(idea.Configure);

			string section = BuildComparisonSection(idea, baseline, variant);
			ApplyToBattleLogic(outputPath, idea.Letter, section);
			Console.WriteLine($"Updated {outputPath} with idea {idea.Letter} results.");
			return 0;
		}

		static string BuildComparisonSection(
			Idea idea, List<WarScenarioCli.AggregatedResult> baseline, List<WarScenarioCli.AggregatedResult> variant) {
			var sb = new StringBuilder();
			sb.AppendLine($"### Idea {idea.Letter}: {idea.Title}");
			sb.AppendLine();
			sb.AppendLine($"Change: {idea.ChangeDescription}");
			sb.AppendLine();
			sb.AppendLine(
				"| Case | War duration | Battle count | Occupied provinces (% of loser's) | Attacker losses (% recruits) | Defender losses (% recruits) | First-battle winner = war winner | Winner |");
			sb.AppendLine("|---|---|---|---|---|---|---|---|");
			for (int i = 0; i < baseline.Count; i++) {
				WarScenarioCli.AggregatedResult b = baseline[i];
				WarScenarioCli.AggregatedResult v = variant[i];
				sb.AppendLine(
					$"| {b.Name} | {Delta(b.AvgDurationDays, v.AvgDurationDays, "d", 0)} | " +
					$"{Delta(b.AvgBattleCount, v.AvgBattleCount, "", 1)} | " +
					$"{Delta(b.AvgLoserProvincesOccupiedPercent, v.AvgLoserProvincesOccupiedPercent, "%", 1)} | " +
					$"{Delta(b.AvgAttackerLossPercent, v.AvgAttackerLossPercent, "%", 1)} | " +
					$"{Delta(b.AvgDefenderLossPercent, v.AvgDefenderLossPercent, "%", 1)} | " +
					$"{DeltaPoints(b.FirstBattleDecidesWarPercent, v.FirstBattleDecidesWarPercent)} | " +
					$"{b.WinnerSummary} -> {v.WinnerSummary} |");
			}
			return sb.ToString();
		}

		// "baseline -> new (+delta%)" — delta% is the relative change vs baseline, sign-explicit.
		static string Delta(double baseValue, double newValue, string unit, int decimals) {
			string b = baseValue.ToString("N" + decimals, CultureInfo.InvariantCulture);
			string n = newValue.ToString("N" + decimals, CultureInfo.InvariantCulture);
			string pct;
			if (Math.Abs(baseValue) < 0.0001) {
				pct = newValue == 0 ? "0.0" : "n/a";
			} else {
				double relative = 100.0 * (newValue - baseValue) / baseValue;
				pct = (relative >= 0 ? "+" : "") + relative.ToString("N1", CultureInfo.InvariantCulture);
			}
			return $"{b}{unit} -> {n}{unit} ({pct}%)";
		}

		// Same "baseline -> new" shape as Delta, but for a value that's already a percentage — reports
		// the plain percentage-point difference instead of a relative %, and handles NaN (no qualifying
		// runs on either side) explicitly.
		static string DeltaPoints(double baseValue, double newValue) {
			string b = double.IsNaN(baseValue) ? "n/a" : $"{baseValue:N1}%";
			string n = double.IsNaN(newValue) ? "n/a" : $"{newValue:N1}%";
			if (double.IsNaN(baseValue) || double.IsNaN(newValue)) {
				return $"{b} -> {n}";
			}
			double points = newValue - baseValue;
			string pts = (points >= 0 ? "+" : "") + points.ToString("N1", CultureInfo.InvariantCulture);
			return $"{b} -> {n} ({pts}pp)";
		}

		// Checks off "- [ ] **<letter>.**" and appends/replaces a "### Idea <letter>:" section right
		// after the "## Ideas" block (before the next "## " heading, if any).
		static void ApplyToBattleLogic(string path, string letter, string section) {
			string content = File.Exists(path) ? File.ReadAllText(path) : "";
			content = content.Replace($"- [ ] **{letter}.**", $"- [x] **{letter}.**");

			string marker = $"### Idea {letter}:";
			int existingStart = content.IndexOf(marker, StringComparison.Ordinal);
			if (existingStart >= 0) {
				int existingEnd = content.IndexOf("\n### Idea ", existingStart + marker.Length, StringComparison.Ordinal);
				if (existingEnd < 0) {
					existingEnd = content.IndexOf("\n## ", existingStart + marker.Length, StringComparison.Ordinal);
				}
				if (existingEnd < 0) {
					existingEnd = content.Length;
				}
				content = content[..existingStart] + section.TrimEnd() + "\n" + content[existingEnd..];
			} else {
				content = content.TrimEnd() + "\n\n" + section.TrimEnd() + "\n";
			}

			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) {
				Directory.CreateDirectory(dir);
			}
			File.WriteAllText(path, content);
		}
	}
}
