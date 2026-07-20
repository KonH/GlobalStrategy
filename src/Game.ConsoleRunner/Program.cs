using System;
using System.IO;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Game.Commands;
using GS.Main;
using ECS.Viewer;
using ECS.Viewer.Server;

namespace GS.Game.ConsoleRunner {
	public static class Program {
		static int Main(string[] args) {
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
				new FileConfig<MapEntryConfig>(Path.Combine(configDir, "map_entry_config.json")),
				new FileConfig<CountryConfig>(Path.Combine(configDir, "country_config.json")),
				new FileConfig<GameSettings>(Path.Combine(configDir, "game_settings.json")),
				new FileConfig<ResourceConfig>(Path.Combine(configDir, "resource_config.json")),
				new FileConfig<OrganizationConfig>(Path.Combine(configDir, "organizations.json")),
				logger: logger,
				initialOrganizationId: initialOrganizationId,
				character: new FileConfig<CharacterConfig>(Path.Combine(configDir, "character_config.json")),
				action: new FileConfig<ActionConfig>(Path.Combine(configDir, "action_config.json")),
				effect: new FileConfig<EffectConfig>(Path.Combine(configDir, "effect_config.json")),
				mapGeometry: new MapGeometryFileConfig(Path.Combine(configDir, "geojson_world.json")),
				province: new FileConfig<ProvinceConfig>(Path.Combine(configDir, "province_config.json")),
				rngSeed: rngSeed,
				participatingOrganizationIds: participatingOrganizationIds);
		}

		static void RunInteractive(string configDir) {
			var ctx = BuildContext(configDir, logger: new ConsoleLogger());
			var logic = new GameLogic(ctx);

			var pauseToken = new PauseToken();
			var observer = new WorldObserver();
			var server = new ViewerServer(observer, pauseToken, () => logic.World);
			server.Start();

			Console.WriteLine("Press Enter to step the game loop, Ctrl+C to exit.");
			while (true) {
				Console.ReadLine();
				if (!pauseToken.IsPaused) {
					logic.Update(1f);
					Console.WriteLine($"Time: {logic.VisualState.Time.CurrentTime:yyyy-MM-dd}");
				} else {
					Console.WriteLine("(paused — use the viewer to resume)");
				}
			}
		}
	}
}
