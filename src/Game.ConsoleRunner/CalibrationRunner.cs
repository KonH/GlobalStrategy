using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GS.Configs.IO;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;

namespace GS.Game.ConsoleRunner {
	public static class CalibrationRunner {
		static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};

		public static int Run(CalibrationOptions options) {
			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(options.ConfigDir, "organizations.json")).Load();
			var orgIds = new List<string>();
			foreach (var entry in orgConfig.Organizations) { orgIds.Add(entry.OrganizationId); }
			if (!orgIds.Contains(options.OrgId)) {
				throw new InvalidOperationException($"Org '{options.OrgId}' is not present in '{options.ConfigDir}/organizations.json'.");
			}

			string winnerOrgId = options.OrgId;
			if (options.Scenario == "lose") {
				winnerOrgId = "";
				foreach (string orgId in orgIds) {
					if (orgId != options.OrgId) { winnerOrgId = orgId; break; }
				}
				if (string.IsNullOrEmpty(winnerOrgId)) {
					throw new InvalidOperationException("The 'lose' scenario requires at least one other organization to win instead of --org.");
				}
			}

			var ctx = Program.BuildContext(
				options.ConfigDir,
				rngSeed: options.Seed,
				participatingOrganizationIds: orgIds,
				initialOrganizationId: options.OrgId);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			logic.Commands.Push(new DebugDiscoverAllCountriesCommand());
			foreach (var country in logic.CountryConfig.Countries) {
				logic.Commands.Push(new ChangeControlCommand {
					OrgId = winnerOrgId,
					CountryId = country.CountryId,
					Delta = logic.MaxControlPool
				});
			}

			float deltaTime = options.HoursPerTick / (float)logic.GameSettings.SpeedMultipliers[0];

			var stopwatch = Stopwatch.StartNew();
			int tickCount = 0;
			while (!logic.IsCompleted) {
				logic.Update(deltaTime);
				tickCount++;

				if (tickCount >= options.MaxTicks) { break; }
				if ((tickCount & 0xFF) == 0 && stopwatch.Elapsed.TotalSeconds >= options.TimeoutSeconds) { break; }
			}

			var result = new CalibrationResult {
				Scenario = options.Scenario,
				OrgId = options.OrgId,
				WinnerOrgId = winnerOrgId,
				Seed = options.Seed,
				Completed = logic.IsCompleted,
				TickCount = tickCount,
				FinalDate = logic.VisualState.Time.CurrentTime.ToString("yyyy-MM-dd"),
				Score = ResourceQuery.GetValue(logic.World, options.OrgId, ResourceDefinitions.OrgScore)
			};

			string json = JsonSerializer.Serialize(result, s_jsonOptions);
			File.WriteAllText(options.Output, json);

			Console.WriteLine(
				$"Calibration run complete: scenario={options.Scenario}, completed={result.Completed}, ticks={tickCount}, " +
				$"finalDate={result.FinalDate}, score={result.Score}, output={options.Output}");

			return result.Completed ? 0 : 1;
		}
	}
}
