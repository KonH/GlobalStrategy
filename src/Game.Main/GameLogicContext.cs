using System.Collections.Generic;
using GS.Configs;
using GS.Game.Configs;

namespace GS.Main {
	public class GameLogicContext {
		public IConfigSource<GeoJsonConfig> GeoJson { get; }
		public IConfigSource<MapEntryConfig> MapEntry { get; }
		public IConfigSource<CountryConfig> Country { get; }
		public IConfigSource<GameSettings> GameSettings { get; }
		public IConfigSource<ResourceConfig> Resource { get; }
		public IConfigSource<OrganizationConfig> Organization { get; }
		public IConfigSource<CharacterConfig> Character { get; }
		public IConfigSource<ActionConfig> Action { get; }
		public IConfigSource<EffectConfig> Effect { get; }
		public IConfigSource<List<GS.Core.Map.MapFeature>>? MapGeometry { get; }
		public IPersistentStorage? Storage { get; }
		public ISnapshotSerializer? Serializer { get; }
		public IGameLogger? Logger { get; }
		public string InitialPlayerCountryId { get; }
		public string InitialOrganizationId { get; }

		public GameLogicContext(
			IConfigSource<GeoJsonConfig> geoJson,
			IConfigSource<MapEntryConfig> mapEntry,
			IConfigSource<CountryConfig> country,
			IConfigSource<GameSettings> gameSettings,
			IConfigSource<ResourceConfig> resource,
			IConfigSource<OrganizationConfig> organization,
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			IGameLogger? logger = null,
			string initialPlayerCountryId = "Russian_Empire",
			string initialOrganizationId = "",
			IConfigSource<CharacterConfig>? character = null,
			IConfigSource<ActionConfig>? action = null,
			IConfigSource<EffectConfig>? effect = null,
			IConfigSource<List<GS.Core.Map.MapFeature>>? mapGeometry = null) {
			GeoJson = geoJson;
			MapEntry = mapEntry;
			Country = country;
			GameSettings = gameSettings;
			Resource = resource;
			Organization = organization;
			Character = character ?? new EmptyCharacterConfig();
			Action = action ?? new EmptyActionConfig();
			Effect = effect ?? new EmptyEffectConfig();
			MapGeometry = mapGeometry;
			Storage = storage;
			Serializer = serializer;
			Logger = logger;
			InitialPlayerCountryId = initialPlayerCountryId;
			InitialOrganizationId = initialOrganizationId;
		}

		sealed class EmptyCharacterConfig : IConfigSource<CharacterConfig> {
			public CharacterConfig Load() => new CharacterConfig();
		}

		sealed class EmptyActionConfig : IConfigSource<ActionConfig> {
			public ActionConfig Load() => new ActionConfig();
		}

		sealed class EmptyEffectConfig : IConfigSource<EffectConfig> {
			public EffectConfig Load() => new EffectConfig();
		}
	}
}
