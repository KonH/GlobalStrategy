using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ECS.Viewer;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using Newtonsoft.Json;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace GS.Game.WebClient.Services {
	public sealed class RemoteGameClient : IGameClient {
		static readonly JsonSerializerSettings JsonSettings = CreateSettings();

		readonly HttpClient _http;
		Dictionary<string, IReadOnlyList<string>> _domainIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

		public RemoteGameClient(HttpClient http) {
			_http = http;
		}

		public bool ShowPauseMenu => false;
		public bool HasSession => true;
		public bool IsFrozen { get; private set; }
		public TimeHud? Time { get; private set; }
		public IReadOnlyList<GameLogEntry>? LogEntries { get; private set; }
		public GameCompletionState? Completion { get; private set; }
		public EcsWorldSnapshot? Snapshot { get; private set; }
		public event Action? Changed;

		public async Task RefreshHudAsync() {
			string json = await _http.GetStringAsync("hud");
			JObject obj = JObject.Parse(json);
			Time = new TimeHud {
				CurrentTime = obj.Value<DateTime?>("CurrentTime") ?? DateTime.MinValue,
				IsPaused = obj.Value<bool?>("IsPaused") ?? false,
				MultiplierIndex = obj.Value<int?>("MultiplierIndex") ?? 0
			};
			var completion = new GameCompletionState();
			string winner = obj.Value<string>("WinnerOrganizationId") ?? "";
			bool completed = obj.Value<bool?>("IsCompleted") ?? false;
			GameResult result = ParseResult(obj["Result"]);
			completion.Set(completed, winner, result);
			Completion = completion;
			LogEntries = obj["Entries"]?.ToObject<List<GameLogEntry>>(JsonSerializer.Create(JsonSettings))
				?? (IReadOnlyList<GameLogEntry>)Array.Empty<GameLogEntry>();

			string pauseJson = await _http.GetStringAsync("pause");
			JObject pause = JObject.Parse(pauseJson);
			IsFrozen = pause.Value<bool?>("paused") ?? false;
			Changed?.Invoke();
		}

		public async Task RefreshSnapshotAsync() {
			string json = await _http.GetStringAsync("snapshot");
			Snapshot = JsonConvert.DeserializeObject<EcsWorldSnapshot>(json, JsonSettings);
			await RefreshDomainIdsAsync();
			Changed?.Invoke();
		}

		public async Task TogglePlayPauseAsync() {
			if (Time == null) {
				return;
			}
			string line = Time.IsPaused ? "Unpause" : "Pause";
			await ExecuteAsync(line);
			await RefreshHudAsync();
		}

		public async Task SetSpeedAsync(int index) {
			await ExecuteAsync($"ChangeTimeMultiplier Index={index}");
			await RefreshHudAsync();
		}

		public async Task SetFrozenAsync(bool frozen) {
			var content = new StringContent($"{{\"paused\":{(frozen ? "true" : "false")}}}", Encoding.UTF8, "application/json");
			await _http.PostAsync("pause", content);
			IsFrozen = frozen;
			Changed?.Invoke();
		}

		public async Task<ExecutionResult> ExecuteAsync(string line) {
			var content = new StringContent(line ?? "", Encoding.UTF8, "text/plain");
			HttpResponseMessage resp = await _http.PostAsync("command", content);
			string json = await resp.Content.ReadAsStringAsync();
			JObject obj = JObject.Parse(json);
			bool success = obj.Value<bool?>("success") ?? false;
			string message = obj.Value<string>("message") ?? "";
			return success ? ExecutionResult.Ok(message) : ExecutionResult.Failure(message);
		}

		public async Task<SuggestionResult> SuggestAsync(string input) {
			string q = Uri.EscapeDataString(input ?? "");
			string json = await _http.GetStringAsync("suggest?q=" + q);
			JObject obj = JObject.Parse(json);
			var kind = Enum.TryParse(obj.Value<string>("kind"), out SuggestionKind parsed) ? parsed : SuggestionKind.None;
			int tokenStart = obj.Value<int?>("tokenStart") ?? (input?.Length ?? 0);
			bool insertLeadingSpace = obj.Value<bool?>("insertLeadingSpace") ?? false;
			var items = new List<SuggestionItem>();
			JToken? arr = obj["items"];
			if (arr is JArray list) {
				foreach (JToken token in list) {
					items.Add(new SuggestionItem(token.Value<string>("value") ?? "", token.Value<string>("label") ?? ""));
				}
			}
			return new SuggestionResult(kind, items, tokenStart, insertLeadingSpace);
		}

		public async Task PatchFieldAsync(int entityId, string typeName, string fieldName, string rawValue) {
			string path = $"entity/{entityId}/component/{Uri.EscapeDataString(typeName)}";
			var content = new StringContent($"{{\"{fieldName}\":{JsonConvert.SerializeObject(rawValue)}}}", Encoding.UTF8, "application/json");
			var req = new HttpRequestMessage(new HttpMethod("PATCH"), path) { Content = content };
			await _http.SendAsync(req);
			await RefreshSnapshotAsync();
		}

		public IReadOnlyList<string> DomainIds(string? domainIdKind, string? ownerType) {
			string key = DomainIdCatalog.CacheKey(domainIdKind, ownerType);
			if (_domainIds.TryGetValue(key, out IReadOnlyList<string>? ids)) {
				return ids;
			}
			if (!string.IsNullOrEmpty(domainIdKind) && _domainIds.TryGetValue(domainIdKind, out ids)) {
				return ids;
			}
			return Array.Empty<string>();
		}

		async Task RefreshDomainIdsAsync() {
			string json = await _http.GetStringAsync("ids");
			JObject obj = JObject.Parse(json);
			var next = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			foreach (JProperty prop in obj.Properties()) {
				if (prop.Value is not JArray arr) {
					continue;
				}
				var list = new List<string>();
				foreach (JToken token in arr) {
					string? value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
					if (!string.IsNullOrEmpty(value)) {
						list.Add(value);
					}
				}
				next[prop.Name] = list;
			}
			_domainIds = next;
		}

		static GameResult ParseResult(JToken? token) {
			if (token == null || token.Type == JTokenType.Null) {
				return GameResult.InProgress;
			}
			if (token.Type == JTokenType.Integer) {
				return (GameResult)token.Value<int>();
			}
			return Enum.TryParse(token.ToString(), out GameResult parsed) ? parsed : GameResult.InProgress;
		}

		static JsonSerializerSettings CreateSettings() {
			var settings = new JsonSerializerSettings();
			settings.Converters.Add(new StringEnumConverter());
			settings.Culture = CultureInfo.InvariantCulture;
			return settings;
		}
	}
}
