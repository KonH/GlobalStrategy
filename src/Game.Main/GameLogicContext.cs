using GS.Configs;
using GS.Game.Configs;

namespace GS.Main {
	public class GameLogicContext {
		public IConfigSource<GeoJsonConfig> GeoJson { get; }
		public IConfigSource<MapEntryConfig> MapEntry { get; }
		public IConfigSource<CountryConfig> Country { get; }
		public IConfigSource<GameSettings> GameSettings { get; }
		public IConfigSource<ResourceConfig> Resource { get; }

		public GameLogicContext(
			IConfigSource<GeoJsonConfig> geoJson,
			IConfigSource<MapEntryConfig> mapEntry,
			IConfigSource<CountryConfig> country,
			IConfigSource<GameSettings> gameSettings,
			IConfigSource<ResourceConfig> resource) {
			GeoJson = geoJson;
			MapEntry = mapEntry;
			Country = country;
			GameSettings = gameSettings;
			Resource = resource;
		}
	}
}
