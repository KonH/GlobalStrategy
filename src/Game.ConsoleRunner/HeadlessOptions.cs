using System;
using System.Collections.Generic;
using System.Globalization;

namespace GS.Game.ConsoleRunner {
	public class HeadlessOptions {
		public bool IsHeadless { get; private set; }
		public int Seed { get; private set; }
		public string Output { get; private set; } = "";
		public IReadOnlyList<string>? OrgIds { get; private set; }
		public string ConfigDir { get; private set; } = "Assets/Configs";
		public DateTime? EndDate { get; private set; }
		public int? MaxTicks { get; private set; }
		public int TimeoutSeconds { get; private set; } = 300;
		public int HoursPerTick { get; private set; } = 24;
		public IReadOnlyList<string> BotProfilePaths { get; private set; } = new List<string>();

		public static HeadlessOptions Parse(string[] args) {
			var options = new HeadlessOptions();
			int? seed = null;
			string? output = null;
			var botPaths = new List<string>();

			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--headless":
						options.IsHeadless = true;
						break;
					case "--seed":
						seed = ParseIntArg(args, ref i, "--seed");
						break;
					case "--output":
						output = NextArg(args, ref i, "--output");
						break;
					case "--orgs":
						string orgsArg = NextArg(args, ref i, "--orgs");
						options.OrgIds = orgsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
						break;
					case "--config-dir":
						options.ConfigDir = NextArg(args, ref i, "--config-dir");
						break;
					case "--end-date":
						string endDateArg = NextArg(args, ref i, "--end-date");
						if (!DateTime.TryParseExact(endDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate)) {
							throw new ArgumentException($"--end-date must be in yyyy-MM-dd format, got '{endDateArg}'.");
						}
						options.EndDate = endDate;
						break;
					case "--max-ticks":
						options.MaxTicks = ParseIntArg(args, ref i, "--max-ticks");
						break;
					case "--timeout-seconds":
						options.TimeoutSeconds = ParseIntArg(args, ref i, "--timeout-seconds");
						break;
					case "--hours-per-tick":
						options.HoursPerTick = ParseIntArg(args, ref i, "--hours-per-tick");
						break;
					case "--bot":
						botPaths.Add(NextArg(args, ref i, "--bot"));
						break;
					default:
						throw new ArgumentException($"Unknown argument '{args[i]}'.");
				}
			}

			options.BotProfilePaths = botPaths;

			if (botPaths.Count > 0 && !options.IsHeadless) {
				throw new ArgumentException("--bot requires --headless.");
			}

			if (!options.IsHeadless) {
				return options;
			}

			if (seed == null) {
				throw new ArgumentException("--headless requires --seed.");
			}
			options.Seed = seed.Value;

			if (string.IsNullOrEmpty(output)) {
				throw new ArgumentException("--headless requires --output.");
			}
			options.Output = output;

			if (options.EndDate == null && options.MaxTicks == null) {
				throw new ArgumentException("--headless requires at least one of --end-date or --max-ticks.");
			}

			if (options.HoursPerTick < 1 || options.HoursPerTick > 672) {
				throw new ArgumentException(
					$"--hours-per-tick must be in [1, 672] (672h = 28 days, the shortest month, to avoid skipping a month boundary), got {options.HoursPerTick}.");
			}

			return options;
		}

		static string NextArg(string[] args, ref int i, string flag) {
			if (i + 1 >= args.Length) {
				throw new ArgumentException($"{flag} requires a value.");
			}
			i++;
			return args[i];
		}

		static int ParseIntArg(string[] args, ref int i, string flag) {
			string raw = NextArg(args, ref i, flag);
			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) {
				throw new ArgumentException($"{flag} must be an integer, got '{raw}'.");
			}
			return value;
		}
	}
}
