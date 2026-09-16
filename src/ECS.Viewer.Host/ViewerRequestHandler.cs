using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ECS;
using ECS.Viewer;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ECS.Viewer.Host {
	public sealed class ViewerRequestHandler {
		static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();
		static readonly HashSet<string> ApiPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"/hud", "/snapshot", "/command", "/suggest", "/pause", "/ids"
		};

		readonly string _staticRoot;
		readonly SimulationMarshal _marshal;
		readonly PauseToken _pauseToken;
		readonly WorldObserver _observer;
		readonly Func<World> _worldAccessor;
		readonly GameLogic? _logic;
		readonly CommandExecutor? _commandExecutor;
		readonly SuggestionEngine? _suggestionEngine;
		readonly Action<string>? _logError;
		bool _missingStaticLogged;

		public ViewerRequestHandler(
			string staticRoot,
			SimulationMarshal marshal,
			PauseToken pauseToken,
			WorldObserver observer,
			Func<World> worldAccessor,
			GameLogic? logic = null,
			CommandExecutor? commandExecutor = null,
			SuggestionEngine? suggestionEngine = null,
			Action<string>? logError = null
		) {
			_staticRoot = WebDebugUiRoot.FromPublishDirectory(staticRoot ?? "");
			_marshal = marshal ?? throw new ArgumentNullException(nameof(marshal));
			_pauseToken = pauseToken ?? throw new ArgumentNullException(nameof(pauseToken));
			_observer = observer ?? throw new ArgumentNullException(nameof(observer));
			_worldAccessor = worldAccessor ?? throw new ArgumentNullException(nameof(worldAccessor));
			_logic = logic;
			_commandExecutor = commandExecutor;
			_suggestionEngine = suggestionEngine;
			_logError = logError;
		}

		public ViewerHttpResult Handle(string method, string path, string query, string body) {
			method = (method ?? "GET").ToUpperInvariant();
			path = NormalizePath(path);

			if (method == "OPTIONS") {
				return Json(200, "{}");
			}

			if (IsApi(method, path)) {
				return _marshal.EnqueueFunc(() => HandleApi(method, path, query, body ?? "")).GetAwaiter().GetResult();
			}

			if (method != "GET") {
				return Json(404, "{\"error\":\"not found\"}");
			}

			return ServeStatic(path);
		}

		ViewerHttpResult HandleApi(string method, string path, string query, string body) {
			if (method == "GET" && path == "/hud") {
				return Json(200, JsonConvert.SerializeObject(BuildHud(), JsonSettings));
			}

			if (method == "GET" && path == "/snapshot") {
				WorldSnapshot snap = _observer.Capture(_worldAccessor(), SnapshotFieldMetadata.Apply);
				return Json(200, JsonConvert.SerializeObject(snap, JsonSettings));
			}

			if (path == "/pause") {
				if (method == "GET") {
					return Json(200, _pauseToken.IsPaused ? "{\"paused\":true}" : "{\"paused\":false}");
				}
				if (method == "POST") {
					if (!string.IsNullOrWhiteSpace(body)) {
						var obj = JObject.Parse(body);
						if (obj.TryGetValue("paused", out JToken? val)) {
							_pauseToken.IsPaused = val.Value<bool>();
						}
					}
					return Json(200, "{}");
				}
			}

			if (method == "POST" && path == "/command") {
				if (_commandExecutor == null || _logic == null) {
					return Json(503, "{\"error\":\"command executor is not available\"}");
				}
				ExecutionResult result = _commandExecutor.Execute(body ?? "", _logic.Commands);
				return Json(200, JsonConvert.SerializeObject(new {
					success = result.Success,
					message = result.Message
				}, JsonSettings));
			}

			if (method == "GET" && path == "/ids") {
				ISuggestionSource? source = _logic != null ? new GameLogicSuggestionSource(_logic) : null;
				if (source == null) {
					return Json(200, "{}");
				}
				string kind = QueryValue(query, "kind");
				string ownerType = QueryValue(query, "ownerType");
				if (!string.IsNullOrEmpty(kind)) {
					IReadOnlyList<string> ids = DomainIdCatalog.IdsFor(source, kind, ownerType);
					return Json(200, JsonConvert.SerializeObject(new Dictionary<string, IReadOnlyList<string>> {
						[DomainIdCatalog.CacheKey(kind, ownerType)] = ids
					}, JsonSettings));
				}
				return Json(200, JsonConvert.SerializeObject(DomainIdCatalog.All(source), JsonSettings));
			}

			if (method == "GET" && path == "/suggest") {
				if (_suggestionEngine == null) {
					return Json(503, "{\"error\":\"suggestion engine is not available\"}");
				}
				string q = QueryValue(query, "q");
				SuggestionResult result = _suggestionEngine.GetSuggestions(q);
				var items = new List<object>(result.Items.Count);
				foreach (SuggestionItem item in result.Items) {
					items.Add(new { value = item.Value, label = item.Label });
				}
				return Json(200, JsonConvert.SerializeObject(new {
					kind = result.Kind.ToString(),
					tokenStart = result.TokenStart,
					insertLeadingSpace = result.InsertLeadingSpace,
					items
				}, JsonSettings));
			}

			if (method == "PATCH") {
				string[] parts = path.Split('/');
				if (parts.Length == 5 && parts[1] == "entity" && parts[3] == "component") {
					if (!int.TryParse(parts[2], out int entityId)) {
						return Json(400, "{\"error\":\"invalid entity id\"}");
					}
					string typeName = Uri.UnescapeDataString(parts[4]);
					JObject obj = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
					World world = _worldAccessor();
					bool anyUpdated = false;
					foreach (JProperty prop in obj.Properties()) {
						if (_observer.TrySetField(world, entityId, typeName, prop.Name, TokenToRaw(prop.Value))) {
							anyUpdated = true;
						}
					}
					if (!anyUpdated) {
						return Json(404, "{\"error\":\"entity or component or field not found\"}");
					}
					return Json(200, "{}");
				}
			}

			return Json(404, "{\"error\":\"not found\"}");
		}

		HudPayload BuildHud() {
			var hud = new HudPayload();
			if (_logic == null) {
				return hud;
			}
			TimeState time = _logic.VisualState.Time;
			GameCompletionState completion = _logic.VisualState.GameCompletion;
			hud.CurrentTime = time.CurrentTime.ToString("O", CultureInfo.InvariantCulture);
			hud.IsPaused = time.IsPaused;
			hud.MultiplierIndex = time.MultiplierIndex;
			hud.Entries = _logic.VisualState.GameLog.Entries;
			hud.IsCompleted = completion.IsCompleted;
			hud.WinnerOrganizationId = completion.WinnerOrganizationId;
			hud.Result = completion.Result;
			return hud;
		}

		ViewerHttpResult ServeStatic(string path) {
			if (string.IsNullOrEmpty(_staticRoot) || !Directory.Exists(_staticRoot)) {
				LogMissingStatic();
				if (path == "/" || path == "") {
					return Json(404, "{\"error\":\"web debug UI is missing; regenerate plugins to publish .tmp/web-debug-ui\"}");
				}
				return Json(404, "{\"error\":\"not found\"}");
			}

			string indexPath = Path.Combine(_staticRoot, "index.html");
			string frameworkDir = Path.Combine(_staticRoot, "_framework");
			if (!File.Exists(indexPath) || !Directory.Exists(frameworkDir)) {
				LogMissingStatic();
				if (path == "/" || path == "") {
					return Json(404, "{\"error\":\"web debug UI is missing; regenerate plugins to publish .tmp/web-debug-ui\"}");
				}
			}

			string relative = path == "/" || path == "" ? "index.html" : path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
			string full = Path.GetFullPath(Path.Combine(_staticRoot, relative));
			string rootFull = Path.GetFullPath(_staticRoot);
			string rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase)) {
				return Json(400, "{\"error\":\"invalid path\"}");
			}

			if (File.Exists(full)) {
				return FileResult(full);
			}

			if (!HasExtension(path) && File.Exists(indexPath)) {
				return FileResult(indexPath);
			}

			if (path == "/" || path == "") {
				return Json(404, "{\"error\":\"web debug UI is missing; regenerate plugins to publish .tmp/web-debug-ui\"}");
			}

			return Json(404, "{\"error\":\"not found\"}");
		}

		void LogMissingStatic() {
			if (_missingStaticLogged) {
				return;
			}
			_missingStaticLogged = true;
			_logError?.Invoke("[ECS Viewer] Missing published Blazor UI at " + _staticRoot + " (need index.html and _framework). Run plugin DLL regeneration.");
		}

		static ViewerHttpResult FileResult(string fullPath) {
			byte[] bytes = File.ReadAllBytes(fullPath);
			return new ViewerHttpResult(200, MimeType(fullPath), bytes);
		}

		static ViewerHttpResult Json(int status, string json) {
			return new ViewerHttpResult(status, "application/json", Encoding.UTF8.GetBytes(json));
		}

		static bool IsApi(string method, string path) {
			if (ApiPaths.Contains(path)) {
				return true;
			}
			if (method == "PATCH" && path.StartsWith("/entity/", StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			return false;
		}

		static string NormalizePath(string? path) {
			if (string.IsNullOrEmpty(path)) {
				return "/";
			}
			if (path.Length > 1) {
				path = path.TrimEnd('/');
			}
			if (!path.StartsWith("/", StringComparison.Ordinal)) {
				path = "/" + path;
			}
			return path;
		}

		static bool HasExtension(string path) {
			int slash = path.LastIndexOf('/');
			string name = slash >= 0 ? path.Substring(slash + 1) : path;
			return name.Contains(".");
		}

		static string QueryValue(string query, string key) {
			if (string.IsNullOrEmpty(query)) {
				return "";
			}
			string qs = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
			foreach (string part in qs.Split('&')) {
				int eq = part.IndexOf('=');
				string name = eq >= 0 ? part.Substring(0, eq) : part;
				string value = eq >= 0 ? part.Substring(eq + 1) : "";
				if (string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase)) {
					return Uri.UnescapeDataString(value.Replace("+", " "));
				}
			}
			return "";
		}

		static string TokenToRaw(JToken token) {
			if (token.Type == JTokenType.String || token.Type == JTokenType.Boolean || token.Type == JTokenType.Integer || token.Type == JTokenType.Float) {
				return token.ToString();
			}
			return token.ToString();
		}

		static string MimeType(string path) {
			string name = Path.GetFileName(path);
			if (string.Equals(name, "blazor.boot.json", StringComparison.OrdinalIgnoreCase)) {
				return "application/json";
			}
			string ext = Path.GetExtension(path).ToLowerInvariant();
			switch (ext) {
				case ".html": return "text/html";
				case ".js": return "application/javascript";
				case ".css": return "text/css";
				case ".json": return "application/json";
				case ".wasm": return "application/wasm";
				case ".dll": return "application/octet-stream";
				case ".dat": return "application/octet-stream";
				case ".blat": return "application/octet-stream";
				case ".pdb": return "application/octet-stream";
				case ".png": return "image/png";
				case ".woff": return "font/woff";
				case ".woff2": return "font/woff2";
				default: return "application/octet-stream";
			}
		}

		static JsonSerializerSettings CreateJsonSettings() {
			var settings = new JsonSerializerSettings();
			settings.ContractResolver = new DefaultContractResolver();
			settings.Converters.Add(new EntityRefValueJsonConverter());
			settings.Converters.Add(new StringEnumConverter());
			return settings;
		}
	}
}
