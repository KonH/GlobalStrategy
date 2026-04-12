using GS.Configs;
using GS.Game.Configs;

namespace GS.Main {
	public class GameLogicContext {
		public IConfigSource<GeoJsonConfig> GeoJson { get; }
		public IConfigSource<MapEntryConfig> MapEntry { get; }
		public IConfigSource<CountryConfig> Country { get; }
		public IConfigSource<GameSettings> GameSettings { get; }
		public IConfigSource<ResourceConfig> Resource { get; }
		public IPersistentStorage? Storage { get; }
		public ISnapshotSerializer? Serializer { get; }
		public string InitialPlayerCountryId { get; }

		public GameLogicContext(
			IConfigSource<GeoJsonConfig> geoJson,
			IConfigSource<MapEntryConfig> mapEntry,
			IConfigSource<CountryConfig> country,
			IConfigSource<GameSettings> gameSettings,
			IConfigSource<ResourceConfig> resource,
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			string initialPlayerCountryId = "Russian_Empire") {
			GeoJson = geoJson;
			MapEntry = mapEntry;
			Country = country;
			GameSettings = gameSettings;
			Resource = resource;
			Storage = storage;
			Serializer = serializer;
			InitialPlayerCountryId = initialPlayerCountryId;
		}
	}
}
