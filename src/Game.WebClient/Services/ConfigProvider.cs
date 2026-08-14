using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GS.Configs;
using GS.Configs.IO;
using GS.Core.Map;
using GS.Game.Configs;

namespace GS.Game.WebClient.Services {
	public class ConfigProvider : IGameConfigSource {
		readonly HttpClient _httpClient;

		public IReadOnlyConfigSource<GeoJsonConfig> GeoJson { get; private set; } = null!;
		public IReadOnlyConfigSource<MapEntryConfig> MapEntry { get; private set; } = null!;
		public IReadOnlyConfigSource<CountryConfig> Country { get; private set; } = null!;
		public IReadOnlyConfigSource<GameSettings> GameSettings { get; private set; } = null!;
		public IReadOnlyConfigSource<ResourceConfig> Resource { get; private set; } = null!;
		public IReadOnlyConfigSource<OrganizationConfig> Organization { get; private set; } = null!;
		public IReadOnlyConfigSource<CharacterConfig> Character { get; private set; } = null!;
		public IReadOnlyConfigSource<ActionConfig> Action { get; private set; } = null!;
		public IReadOnlyConfigSource<EffectConfig> Effect { get; private set; } = null!;
		public IReadOnlyConfigSource<TasksConfig> Tasks { get; private set; } = null!;
		public IReadOnlyConfigSource<ProvinceConfig> Province { get; private set; } = null!;
		public List<MapFeature> MapGeometry { get; private set; } = null!;

		public ConfigProvider(HttpClient httpClient) {
			_httpClient = httpClient;
		}

		public async Task InitializeAsync() {
			string geoJsonText = await FetchAsync("configs/geojson_world.json");
			GeoJson = new StringConfig<GeoJsonConfig>(geoJsonText);
			MapGeometry = GeoJsonParser.Parse(geoJsonText);
			MapEntry = new StringConfig<MapEntryConfig>(await FetchAsync("configs/map_entry_config.json"));
			Country = new StringConfig<CountryConfig>(await FetchAsync("configs/country_config.json"));
			GameSettings = new StringConfig<GameSettings>(await FetchAsync("configs/game_settings.json"));
			Resource = new StringConfig<ResourceConfig>(await FetchAsync("configs/resource_config.json"));
			Organization = new StringConfig<OrganizationConfig>(await FetchAsync("configs/organizations.json"));
			Character = new StringConfig<CharacterConfig>(await FetchAsync("configs/character_config.json"));
			Action = new StringConfig<ActionConfig>(await FetchAsync("configs/action_config.json"));
			Effect = new StringConfig<EffectConfig>(await FetchAsync("configs/effect_config.json"));
			Tasks = new StringConfig<TasksConfig>(await FetchAsync("configs/tasks_config.json"));
			Province = new StringConfig<ProvinceConfig>(await FetchAsync("configs/province_config.json"));
		}

		async Task<string> FetchAsync(string relativePath) {
			return await _httpClient.GetStringAsync(relativePath);
		}
	}
}
