using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GS.Game.WebClient.Services {
	// Web-side counterpart of the Unity ILocalization: loads a flat {key: value}
	// JSON map extracted at build time from Assets/Localization/{locale}.asset
	// (see Game.WebClient.LocaleTool) and exposes it as Get(key).
	public class Localization : ILocalization {
		readonly HttpClient _httpClient;
		Dictionary<string, string> _entries = new();

		public string CurrentLocale { get; private set; } = "en";

		public Localization(HttpClient httpClient) {
			_httpClient = httpClient;
		}

		public Task InitializeAsync() {
			return SetLocaleAsync(CurrentLocale);
		}

		public async Task SetLocaleAsync(string locale) {
			string json = await _httpClient.GetStringAsync($"locales/{locale}.json");
			var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
			_entries = loaded ?? new Dictionary<string, string>();
			CurrentLocale = locale;
		}

		public string Get(string key) {
			if (_entries.TryGetValue(key, out string? value)) {
				return value;
			}
			return $"[{key}]";
		}
	}
}
