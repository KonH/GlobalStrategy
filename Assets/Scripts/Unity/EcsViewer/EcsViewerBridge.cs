using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ECS;
using ECS.Viewer;
using GS.Main;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VContainer;

namespace GS.Unity.EcsViewer {
	public class EcsViewerBridge : MonoBehaviour {
		[SerializeField] bool _enabled = true;

		public static string? CurrentUrl { get; private set; }

		GameLogic _logic;
		PauseToken _pauseToken;
		WorldObserver _observer;
		HttpListener _listener;

		[Inject]
		void Construct(GameLogic logic, PauseToken pauseToken) {
			_logic = logic;
			_pauseToken = pauseToken;
		}

		void Awake() {
#if !UNITY_WEBGL
			if (!_enabled) {
				return;
			}
			_observer = new WorldObserver();
			int port = FindFreePort();
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{port}/");
			_listener.Start();
			CurrentUrl = $"http://localhost:{port}";
			Debug.Log($"[ECS Viewer] {CurrentUrl}");
			Task.Run(Loop);
#endif
		}

		void OnDestroy() {
			_listener?.Stop();
			CurrentUrl = null;
		}

#if !UNITY_WEBGL
		async Task Loop() {
			while (_listener != null && _listener.IsListening) {
				HttpListenerContext ctx;
				try {
					ctx = await _listener.GetContextAsync();
				} catch {
					break;
				}
				_ = Task.Run(() => Handle(ctx));
			}
		}

		void Handle(HttpListenerContext ctx) {
			try {
				HandleInner(ctx);
			} catch (Exception ex) {
				try { Respond(ctx.Response, 500, $"{{\"error\":\"{ex.Message}\"}}"); } catch { }
			}
		}

		void HandleInner(HttpListenerContext ctx) {
			string method = ctx.Request.HttpMethod.ToUpperInvariant();
			string path = ctx.Request.Url?.AbsolutePath.TrimEnd('/') ?? "/";

			if (method == "GET" && (path == "" || path == "/")) {
				ServeFile(ctx.Response, "index.html", "text/html");
				return;
			}
			if (method == "GET" && path == "/app.js") {
				ServeFile(ctx.Response, "app.js", "application/javascript");
				return;
			}
			if (method == "GET" && path == "/snapshot") {
				ECS.Viewer.WorldSnapshot snap = _observer.Capture(_logic.World);
				string json = SerializeSnapshot(snap);
				Respond(ctx.Response, 200, json);
				return;
			}
			if (path == "/pause") {
				if (method == "GET") {
					Respond(ctx.Response, 200, $"{{\"paused\":{(_pauseToken.IsPaused ? "true" : "false")}}}");
					return;
				}
				if (method == "POST") {
					string body = ReadBody(ctx.Request);
					var obj = JObject.Parse(body);
					if (obj.TryGetValue("paused", out var val)) {
						_pauseToken.IsPaused = val.Value<bool>();
					}
					Respond(ctx.Response, 200, "{}");
					return;
				}
			}
			if (method == "PATCH") {
				var parts = path.Split('/');
				if (parts.Length == 5 && parts[1] == "entity" && parts[3] == "component") {
					if (!int.TryParse(parts[2], out int entityId)) {
						Respond(ctx.Response, 400, "{\"error\":\"invalid entity id\"}");
						return;
					}
					string typeName = Uri.UnescapeDataString(parts[4]);
					string body = ReadBody(ctx.Request);
					var obj = JObject.Parse(body);
					bool any = false;
					foreach (var prop in obj.Properties()) {
						if (_observer.TrySetField(_logic.World, entityId, typeName, prop.Name, prop.Value.ToString())) {
							any = true;
						}
					}
					if (!any) {
						Respond(ctx.Response, 404, "{\"error\":\"entity or component or field not found\"}");
						return;
					}
					Respond(ctx.Response, 200, "{}");
					return;
				}
			}
			Respond(ctx.Response, 404, "{\"error\":\"not found\"}");
		}

		// Serve static files from StreamingAssets/EcsViewer/
		static void ServeFile(HttpListenerResponse resp, string filename, string contentType) {
			string path = System.IO.Path.Combine(Application.streamingAssetsPath, "EcsViewer", filename);
			if (!File.Exists(path)) {
				Respond(resp, 404, "not found");
				return;
			}
			byte[] bytes = File.ReadAllBytes(path);
			resp.ContentType = contentType;
			resp.ContentLength64 = bytes.Length;
			resp.OutputStream.Write(bytes, 0, bytes.Length);
			resp.OutputStream.Close();
		}

		static string SerializeSnapshot(ECS.Viewer.WorldSnapshot snap) {
			var settings = new JsonSerializerSettings();
			settings.Converters.Add(new EntityRefValueJsonConverter());
			return JsonConvert.SerializeObject(snap, settings);
		}

		static void Respond(HttpListenerResponse resp, int status, string body) {
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			resp.StatusCode = status;
			resp.ContentType = "application/json";
			resp.Headers["Access-Control-Allow-Origin"] = "*";
			resp.ContentLength64 = bytes.Length;
			resp.OutputStream.Write(bytes, 0, bytes.Length);
			resp.OutputStream.Close();
		}

		static string ReadBody(HttpListenerRequest req) {
			using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
			return reader.ReadToEnd();
		}

		static int FindFreePort() {
			var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			l.Start();
			int port = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			return port;
		}
#endif
	}

	class EntityRefValueJsonConverter : JsonConverter<EntityRefValue> {
		public override EntityRefValue ReadJson(JsonReader reader, Type objectType, EntityRefValue existingValue, bool hasExistingValue, JsonSerializer serializer) {
			throw new NotSupportedException();
		}
		public override void WriteJson(JsonWriter writer, EntityRefValue value, JsonSerializer serializer) {
			writer.WriteStartObject();
			writer.WritePropertyName("__entityRef");
			writer.WriteValue(value.EntityId);
			writer.WriteEndObject();
		}
	}
}
