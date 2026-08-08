using System.Collections.Generic;
using GS.Configs;
using GS.Core.Map;
using GS.Game.Configs;

namespace GS.Game.WebClient.Services {
	// Seam between GameSession and however the ten config JSONs + map geometry were
	// loaded. ConfigProvider is the production implementation (HttpClient fetch);
	// Game.WebClient.Tests provides a file-based implementation so GameSession can be
	// exercised headlessly without a browser/HttpClient.
	public interface IGameConfigSource {
		IReadOnlyConfigSource<GeoJsonConfig> GeoJson { get; }
		IReadOnlyConfigSource<MapEntryConfig> MapEntry { get; }
		IReadOnlyConfigSource<CountryConfig> Country { get; }
		IReadOnlyConfigSource<GameSettings> GameSettings { get; }
		IReadOnlyConfigSource<ResourceConfig> Resource { get; }
		IReadOnlyConfigSource<OrganizationConfig> Organization { get; }
		IReadOnlyConfigSource<CharacterConfig> Character { get; }
		IReadOnlyConfigSource<ActionConfig> Action { get; }
		IReadOnlyConfigSource<EffectConfig> Effect { get; }
		IReadOnlyConfigSource<TasksConfig> Tasks { get; }
		IReadOnlyConfigSource<ProvinceConfig> Province { get; }
		List<MapFeature> MapGeometry { get; }
	}
}
