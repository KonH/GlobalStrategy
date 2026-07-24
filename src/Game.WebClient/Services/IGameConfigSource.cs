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
		IConfigSource<GeoJsonConfig> GeoJson { get; }
		IConfigSource<MapEntryConfig> MapEntry { get; }
		IConfigSource<CountryConfig> Country { get; }
		IConfigSource<GameSettings> GameSettings { get; }
		IConfigSource<ResourceConfig> Resource { get; }
		IConfigSource<OrganizationConfig> Organization { get; }
		IConfigSource<CharacterConfig> Character { get; }
		IConfigSource<ActionConfig> Action { get; }
		IConfigSource<EffectConfig> Effect { get; }
		IConfigSource<ProvinceConfig> Province { get; }
		List<MapFeature> MapGeometry { get; }
	}
}
