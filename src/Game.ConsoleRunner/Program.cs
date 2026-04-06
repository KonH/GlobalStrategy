using System;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Game.Commands;
using GS.Main;

namespace GS.Game.ConsoleRunner {
	static class Program {
		static void Main() {
			var ctx = new GameLogicContext(
				new FileConfig<GeoJsonConfig>("data/geojson_world.json"),
				new FileConfig<MapEntryConfig>("data/map_entry_config.json"),
				new FileConfig<CountryConfig>("data/country_config.json"),
				new FileConfig<GameSettings>("data/game_settings.json")
			);
			var logic = new GameLogic(ctx);
			logic.Commands.Push(new SelectCountryCommand("FR"));
			logic.Update(0f);
			Console.WriteLine(logic.VisualState.SelectedCountry.CountryId); // "FR"
		}
	}
}
