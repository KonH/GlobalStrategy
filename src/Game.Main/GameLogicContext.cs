using System.Collections.Generic;
using GS.Configs;
using GS.Game.Configs;

namespace GS.Main {
	public class GameLogicContext {
		public IReadOnlyConfigSource<GeoJsonConfig> GeoJson { get; }
		public IReadOnlyConfigSource<MapEntryConfig> MapEntry { get; }
		public IReadOnlyConfigSource<CountryConfig> Country { get; }
		public IReadOnlyConfigSource<GameSettings> GameSettings { get; }
		public IReadOnlyConfigSource<ResourceConfig> Resource { get; }
		public IReadOnlyConfigSource<OrganizationConfig> Organization { get; }
		public IReadOnlyConfigSource<CharacterConfig> Character { get; }
		public IReadOnlyConfigSource<ActionConfig> Action { get; }
		public IReadOnlyConfigSource<EffectConfig> Effect { get; }
		public IReadOnlyConfigSource<TasksConfig> Tasks { get; }
		public IReadOnlyConfigSource<ProvinceConfig> Province { get; }
		public IReadOnlyConfigSource<List<GS.Core.Map.MapFeature>>? MapGeometry { get; }
		public IPersistentStorage? Storage { get; }
		public ISnapshotSerializer? Serializer { get; }
		public IGameLogger? Logger { get; }
		public string InitialOrganizationId { get; }
		public int? RngSeed { get; }
		public IReadOnlyList<string>? ParticipatingOrganizationIds { get; }
		public string? InitialLocale { get; }

		public GameLogicContext(
			IReadOnlyConfigSource<GeoJsonConfig> geoJson,
			IReadOnlyConfigSource<MapEntryConfig> mapEntry,
			IReadOnlyConfigSource<CountryConfig> country,
			IReadOnlyConfigSource<GameSettings> gameSettings,
			IReadOnlyConfigSource<ResourceConfig> resource,
			IReadOnlyConfigSource<OrganizationConfig> organization,
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			IGameLogger? logger = null,
			string initialOrganizationId = "",
			IReadOnlyConfigSource<CharacterConfig>? character = null,
			IReadOnlyConfigSource<ActionConfig>? action = null,
			IReadOnlyConfigSource<EffectConfig>? effect = null,
			IReadOnlyConfigSource<TasksConfig>? tasks = null,
			IReadOnlyConfigSource<List<GS.Core.Map.MapFeature>>? mapGeometry = null,
			IReadOnlyConfigSource<ProvinceConfig>? province = null,
			int? rngSeed = null,
			IReadOnlyList<string>? participatingOrganizationIds = null,
			string? initialLocale = null) {
			GeoJson = geoJson;
			MapEntry = mapEntry;
			Country = country;
			GameSettings = gameSettings;
			Resource = resource;
			Organization = organization;
			Character = character ?? new EmptyCharacterConfig();
			Action = action ?? new EmptyActionConfig();
			Effect = effect ?? new EmptyEffectConfig();
			Tasks = tasks ?? new EmptyTasksConfig();
			Province = province ?? new EmptyProvinceConfig();
			MapGeometry = mapGeometry;
			Storage = storage;
			Serializer = serializer;
			Logger = logger;
			InitialOrganizationId = initialOrganizationId;
			RngSeed = rngSeed;
			ParticipatingOrganizationIds = participatingOrganizationIds;
			InitialLocale = initialLocale;
		}

		sealed class EmptyCharacterConfig : IReadOnlyConfigSource<CharacterConfig> {
			public CharacterConfig Load() => new CharacterConfig();
		}

		sealed class EmptyActionConfig : IReadOnlyConfigSource<ActionConfig> {
			public ActionConfig Load() => new ActionConfig();
		}

		sealed class EmptyEffectConfig : IReadOnlyConfigSource<EffectConfig> {
			public EffectConfig Load() => new EffectConfig();
		}

		sealed class EmptyTasksConfig : IReadOnlyConfigSource<TasksConfig> {
			public TasksConfig Load() => new TasksConfig();
		}

		sealed class EmptyProvinceConfig : IReadOnlyConfigSource<ProvinceConfig> {
			public ProvinceConfig Load() => new ProvinceConfig();
		}
	}
}
