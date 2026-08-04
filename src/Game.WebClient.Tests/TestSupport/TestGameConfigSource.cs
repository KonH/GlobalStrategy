using System;
using System.Collections.Generic;
using System.IO;
using GS.Configs;
using GS.Configs.IO;
using GS.Core.Map;
using GS.Game.Configs;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Tests.TestSupport {
	// Shared IGameConfigSource backed by the real Assets/Configs/*.json via FileConfig<T> - the
	// same repo-root-walking pattern GameSessionTests/StringConfigParityTests use to locate
	// Assets/Configs from the test assembly's output directory. Lets suggestion-provider and
	// SuggestionEngine tests exercise real game data without a browser/HttpClient.
	public sealed class TestGameConfigSource : IGameConfigSource {
		public IReadOnlyConfigSource<GeoJsonConfig> GeoJson { get; }
		public IReadOnlyConfigSource<MapEntryConfig> MapEntry { get; }
		public IReadOnlyConfigSource<CountryConfig> Country { get; }
		public IReadOnlyConfigSource<GameSettings> GameSettings { get; }
		public IReadOnlyConfigSource<ResourceConfig> Resource { get; }
		public IReadOnlyConfigSource<OrganizationConfig> Organization { get; }
		public IReadOnlyConfigSource<CharacterConfig> Character { get; }
		public IReadOnlyConfigSource<ActionConfig> Action { get; }
		public IReadOnlyConfigSource<EffectConfig> Effect { get; }
		public IReadOnlyConfigSource<ProvinceConfig> Province { get; }
		public List<MapFeature> MapGeometry { get; }

		public TestGameConfigSource() {
			string geoJsonPath = FindRepoRootConfigPath("geojson_world.json");
			GeoJson = new FileConfig<GeoJsonConfig>(geoJsonPath);
			MapEntry = new FileConfig<MapEntryConfig>(FindRepoRootConfigPath("map_entry_config.json"));
			Country = new FileConfig<CountryConfig>(FindRepoRootConfigPath("country_config.json"));
			GameSettings = new FileConfig<GameSettings>(FindRepoRootConfigPath("game_settings.json"));
			Resource = new FileConfig<ResourceConfig>(FindRepoRootConfigPath("resource_config.json"));
			Organization = new FileConfig<OrganizationConfig>(FindRepoRootConfigPath("organizations.json"));
			Character = new FileConfig<CharacterConfig>(FindRepoRootConfigPath("character_config.json"));
			Action = new FileConfig<ActionConfig>(FindRepoRootConfigPath("action_config.json"));
			Effect = new FileConfig<EffectConfig>(FindRepoRootConfigPath("effect_config.json"));
			Province = new FileConfig<ProvinceConfig>(FindRepoRootConfigPath("province_config.json"));
			MapGeometry = GeoJsonParser.Parse(File.ReadAllText(geoJsonPath));
		}

		public static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) {
					return candidate;
				}
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}
	}

	// Deterministic stand-in for the real Localization service - returns the same missing-key
	// bracket format ("[key]") the production Get(key) falls back to, so tests can assert exact
	// expected labels from real config ids without needing wwwroot/locales/*.json.
	public sealed class FakeLocalization : ILocalization {
		public string CurrentLocale => "en";

		public string Get(string key) {
			return $"[{key}]";
		}
	}
}
