using System;
using System.Globalization;

namespace GS.Game.ConsoleRunner {
	public class CalibrationOptions {
		public string ConfigDir { get; private set; } = "Assets/Configs";
		public string Scenario { get; private set; } = "";
		public string OrgId { get; private set; } = "";
		public int Seed { get; private set; }
		public string Output { get; private set; } = "";
		public int MaxTicks { get; private set; } = 20000;
		public int TimeoutSeconds { get; private set; } = 300;
		public int HoursPerTick { get; private set; } = 24;

		public static CalibrationOptions Parse(string[] args) {
			var options = new CalibrationOptions();
			string? scenario = null;
			string? orgId = null;
			int? seed = null;
			string? output = null;

			// args[0] is the "calibrate-end-game" verb itself; flags start at index 1.
			for (int i = 1; i < args.Length; i++) {
				switch (args[i]) {
					case "--config":
						options.ConfigDir = NextArg(args, ref i, "--config");
						break;
					case "--scenario":
						scenario = NextArg(args, ref i, "--scenario");
						break;
					case "--org":
						orgId = NextArg(args, ref i, "--org");
						break;
					case "--seed":
						seed = ParseIntArg(args, ref i, "--seed");
						break;
					case "--output":
						output = NextArg(args, ref i, "--output");
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
					default:
						throw new ArgumentException($"Unknown argument '{args[i]}'.");
				}
			}

			if (scenario == null) {
				throw new ArgumentException("calibrate-end-game requires --scenario.");
			}
			if (scenario != "win" && scenario != "lose") {
				throw new ArgumentException($"--scenario must be 'win' or 'lose', got '{scenario}'.");
			}
			options.Scenario = scenario;

			if (string.IsNullOrEmpty(orgId)) {
				throw new ArgumentException("calibrate-end-game requires --org.");
			}
			options.OrgId = orgId;

			if (seed == null) {
				throw new ArgumentException("calibrate-end-game requires --seed.");
			}
			options.Seed = seed.Value;

			if (string.IsNullOrEmpty(output)) {
				throw new ArgumentException("calibrate-end-game requires --output.");
			}
			options.Output = output;

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
