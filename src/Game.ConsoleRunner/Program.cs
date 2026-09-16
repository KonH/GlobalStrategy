using System;
using System.IO;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Game.Commands;
using GS.Main;
using ECS.Viewer;
using ECS.Viewer.Host;
using ECS.Viewer.Server;

using GS.Game.Systems;
using GS.Game.ConsoleRunner.WarSim;

namespace GS.Game.ConsoleRunner {
	public static class Program {
		static int Main(string[] args) {
			if (args.Length > 0 && args[0] == "war-scenarios") {
				try {
					return WarScenarioCli.Run(args);
				} catch (Exception ex) {
					Console.Error.WriteLine($"War scenario run failed: {ex.Message}");
					Console.Error.WriteLine(ex.StackTrace);
					return 1;
				}
			}

			if (args.Length > 0 && args[0] == "war-idea") {
				try {
					return WarIdeaCli.Run(args);
				} catch (Exception ex) {
					Console.Error.WriteLine($"War idea run failed: {ex.Message}");
					Console.Error.WriteLine(ex.StackTrace);
					return 1;
				}
			}

			if (args.Length > 0 && args[0] == "calibrate-end-game") {
				try {
					var calibrationOptions = CalibrationOptions.Parse(args);
					return CalibrationRunner.Run(calibrationOptions);
				} catch (ArgumentException ex) {
					Console.Error.WriteLine($"Usage error: {ex.Message}");
					return 1;
				} catch (Exception ex) {
					Console.Error.WriteLine($"Calibration run failed: {ex.Message}");
					return 1;
				}
			}

			HeadlessOptions options;
			try {
				options = HeadlessOptions.Parse(args);
			} catch (ArgumentException ex) {
				Console.Error.WriteLine($"Usage error: {ex.Message}");
				return 1;
			}

			if (options.IsHeadless) {
				try {
					return HeadlessRunner.Run(options);
				} catch (Exception ex) {
					Console.Error.WriteLine($"Headless run failed: {ex.Message}");
					return 1;
				}
			}

			RunInteractive(options.ConfigDir);
			return 0;
		}

		public static GameLogicContext BuildContext(
			string configDir, int? rngSeed = null, System.Collections.Generic.IReadOnlyList<string>? participatingOrganizationIds = null,
			string initialOrganizationId = "", IGameLogger? logger = null) {
			return new GameLogicContext(
				new FileConfig<GeoJsonConfig>(Path.Combine(configDir, "geojson_world.json")),
				new FileConfig<MapEntryConfig>(Path.Combine(configDir, "map_entries.json")),
				new FileConfig<CountryConfig>(Path.Combine(configDir, "countries.json")),
				new FileConfig<GameSettings>(Path.Combine(configDir, "game_settings.json")),
				new FileConfig<ResourceConfig>(Path.Combine(configDir, "resources.json")),
				new FileConfig<OrganizationConfig>(Path.Combine(configDir, "organizations.json")),
				logger: logger,
				initialOrganizationId: initialOrganizationId,
				character: new FileConfig<CharacterConfig>(Path.Combine(configDir, "characters.json")),
				action: new FileConfig<ActionConfig>(Path.Combine(configDir, "actions.json")),
				effect: new FileConfig<EffectConfig>(Path.Combine(configDir, "effects.json")),
				tasks: new FileConfig<TasksConfig>(Path.Combine(configDir, "tasks.json")),
				mapGeometry: new MapGeometryFileConfig(Path.Combine(configDir, "geojson_world.json")),
				province: new FileConfig<ProvinceConfig>(Path.Combine(configDir, "provinces.json")),
				rngSeed: rngSeed,
				participatingOrganizationIds: participatingOrganizationIds);
		}

		static void RunInteractive(string configDir) {
			var ctx = BuildContext(configDir, logger: new ConsoleLogger());
			var logic = new GameLogic(ctx);
			var session = GS.Game.Bots.BotSession.Create(logic, rngSeed: unchecked((int)DateTime.UtcNow.Ticks), logger: new ConsoleLogger());

			var pauseToken = new PauseToken();
			var marshal = new SimulationMarshal();
			var observer = new WorldObserver();
			var registry = new GS.Game.Commands.Text.CommandRegistry();
			var executor = new GS.Game.Commands.Text.CommandExecutor(registry);
			var source = new GS.Game.Commands.Text.Suggestions.GameLogicSuggestionSource(logic);
			var engine = new GS.Game.Commands.Text.Suggestions.SuggestionEngine(
				registry,
				new GS.Game.Commands.Text.Suggestions.SuggestionValueResolver(
					source,
					new GS.Game.Commands.Text.Suggestions.DisplayNameSuggestionLabels()));
			string webRoot = FindWebDebugUi();
			var handler = new ECS.Viewer.Host.ViewerRequestHandler(
				webRoot,
				marshal,
				pauseToken,
				observer,
				() => logic.World,
				logic,
				executor,
				engine,
				msg => Console.Error.WriteLine(msg));
			var server = new ViewerServer(handler);
			server.Start();
			Console.WriteLine($"http://localhost:{server.Port}?host=remote");

			new WallClockSimLoop().Run(session, marshal, pauseToken);
		}

		static string FindWebDebugUi() {
			var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, ".tmp", "web-debug-ui");
				if (WebDebugUiRoot.LooksPublished(WebDebugUiRoot.FromPublishDirectory(candidate))) {
					return candidate;
				}
				dir = dir.Parent;
			}
			return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".tmp", "web-debug-ui"));
		}
	}
}
