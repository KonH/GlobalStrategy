using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Main;

namespace GS.Game.ConsoleRunner {
	static class HeadlessRunner {
		static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};

		public static int Run(HeadlessOptions options) {
			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(options.ConfigDir, "organizations.json")).Load();
			List<string> orgIds;
			if (options.OrgIds != null && options.OrgIds.Count > 0) {
				orgIds = new List<string>(options.OrgIds);
			} else {
				orgIds = new List<string>();
				foreach (var entry in orgConfig.Organizations) { orgIds.Add(entry.OrganizationId); }
			}

			var logger = new ConsoleLogger();
			string initialOrganizationId = orgIds.Count > 0 ? orgIds[0] : "";
			var ctx = Program.BuildContext(
				options.ConfigDir,
				rngSeed: options.Seed,
				participatingOrganizationIds: orgIds,
				initialOrganizationId: initialOrganizationId,
				logger: logger);
			var logic = new GameLogic(ctx);

			var settings = new FileConfig<GameSettings>(Path.Combine(options.ConfigDir, "game_settings.json")).Load();
			float deltaTime = options.HoursPerTick / (float)settings.SpeedMultipliers[0];

			var result = new SimulationResult {
				Seed = options.Seed,
				Parameters = new SimulationParameters {
					OrgIds = orgIds,
					ConfigDir = options.ConfigDir,
					HoursPerTick = options.HoursPerTick,
					DeltaTime = deltaTime,
					EndDate = options.EndDate?.ToString("yyyy-MM-dd"),
					MaxTicks = options.MaxTicks,
					TimeoutSeconds = options.TimeoutSeconds
				}
			};

			var stopwatch = Stopwatch.StartNew();
			int tickCount = 0;
			string endReason = "maxTicks";
			(int month, int year)? lastSampledMonth = null;

			// t0 baseline sample, before any Update() advances time.
			logic.Update(0f);
			AppendTimelineSampleIfNewMonth(logic, orgIds, result.Timeline, ref lastSampledMonth, force: true);

			while (true) {
				logic.Update(deltaTime);
				tickCount++;

				AppendTimelineSampleIfNewMonth(logic, orgIds, result.Timeline, ref lastSampledMonth, force: false);

				DateTime currentDate = logic.VisualState.Time.CurrentTime;
				if (options.EndDate.HasValue && currentDate >= options.EndDate.Value) {
					endReason = "dateReached";
					break;
				}
				if (options.MaxTicks.HasValue && tickCount >= options.MaxTicks.Value) {
					endReason = "maxTicks";
					break;
				}
				if ((tickCount & 0xFF) == 0 && stopwatch.Elapsed.TotalSeconds >= options.TimeoutSeconds) {
					endReason = "timeout";
					break;
				}
			}

			result.TickCount = tickCount;
			result.EndReason = endReason;
			result.FinalDate = logic.VisualState.Time.CurrentTime.ToString("yyyy-MM-dd");
			foreach (string orgId in orgIds) {
				result.Orgs.Add(BuildOrgMetrics(logic, orgId));
			}

			string json = JsonSerializer.Serialize(result, s_jsonOptions);
			File.WriteAllText(options.Output, json);

			Console.WriteLine($"Headless run complete: ticks={tickCount}, endReason={endReason}, finalDate={result.FinalDate}, output={options.Output}");
			return 0;
		}

		static void AppendTimelineSampleIfNewMonth(
			GameLogic logic, List<string> orgIds, List<TimelineSample> timeline,
			ref (int month, int year)? lastSampledMonth, bool force) {
			DateTime currentDate = logic.VisualState.Time.CurrentTime;
			var currentMonth = (currentDate.Month, currentDate.Year);
			if (!force && lastSampledMonth.HasValue && lastSampledMonth.Value == currentMonth) {
				return;
			}
			lastSampledMonth = currentMonth;

			var sample = new TimelineSample { Date = currentDate.ToString("yyyy-MM-dd") };
			foreach (string orgId in orgIds) {
				sample.Orgs.Add(BuildOrgMetrics(logic, orgId));
			}
			timeline.Add(sample);
		}

		static OrgMetricsResult BuildOrgMetrics(GameLogic logic, string orgId) {
			return new OrgMetricsResult {
				OrgId = orgId,
				TotalControl = GS.Game.Systems.OrgMetrics.GetTotalControl(logic.World, orgId),
				Gold = GS.Game.Systems.OrgMetrics.GetGold(logic.World, orgId)
			};
		}
	}
}
