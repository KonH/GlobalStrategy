using System;
using System.Threading;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Game.Commands;
using GS.Main;
using ECS.Viewer;
using ECS.Viewer.Server;

namespace GS.Game.ConsoleRunner {
	static class Program {
		static void Main() {
			var ctx = new GameLogicContext(
				new FileConfig<GeoJsonConfig>("data/geojson_world.json"),
				new FileConfig<MapEntryConfig>("data/map_entry_config.json"),
				new FileConfig<CountryConfig>("data/country_config.json"),
				new FileConfig<GameSettings>("data/game_settings.json"),
				new FileConfig<ResourceConfig>("data/resource_config.json")
			);
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
