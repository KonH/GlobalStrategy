using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ECS.Viewer;

namespace ECS.Viewer.Server {
	public class ViewerServer {
		readonly WorldObserver _observer;
		readonly PauseToken _pauseToken;
		readonly Func<World> _worldAccessor;
		HttpListener? _listener;
		public int Port { get; private set; }

		static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
			WriteIndented = false,
			Converters = { new EntityRefValueConverter() }
		};

		public ViewerServer(WorldObserver observer, PauseToken pauseToken, Func<World> worldAccessor) {
			_observer = observer;
			_pauseToken = pauseToken;
			_worldAccessor = worldAccessor;
		}

		public void Start() {
			Port = FindFreePort();
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{Port}/");
			_listener.Start();
			Console.WriteLine($"[ECS Viewer] http://localhost:{Port}");
			Task.Run(Loop);
		}

		public void Stop() {
			_listener?.Stop();
		}

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
				try {
					Respond(ctx.Response, 500, $"{{\"error\":\"{ex.Message}\"}}");
				} catch { }
			}
		}

		void HandleInner(HttpListenerContext ctx) {
			string method = ctx.Request.HttpMethod.ToUpperInvariant();
			string path = ctx.Request.Url?.AbsolutePath.TrimEnd('/') ?? "/";

			if (method == "GET" && (path == "" || path == "/")) {
				ServeEmbedded(ctx.Response, "ECS.Viewer.Server.Web.index.html", "text/html");
				return;
			}
			if (method == "GET" && path == "/app.js") {
				ServeEmbedded(ctx.Response, "ECS.Viewer.Server.Web.app.js", "application/javascript");
				return;
			}
			if (method == "GET" && path == "/snapshot") {
				WorldSnapshot snap = _observer.Capture(_worldAccessor());
				string json = JsonSerializer.Serialize(snap, _jsonOptions);
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
					using var doc = JsonDocument.Parse(body);
					if (doc.RootElement.TryGetProperty("paused", out var el)) {
						_pauseToken.IsPaused = el.GetBoolean();
					}
					Respond(ctx.Response, 200, "{}");
					return;
				}
			}
			// PATCH /entity/{id}/component/{typeName}
			if (method == "PATCH") {
				var parts = path.Split('/');
				// parts: ["", "entity", "{id}", "component", "{typeName}"]
				if (parts.Length == 5 && parts[1] == "entity" && parts[3] == "component") {
					if (!int.TryParse(parts[2], out int entityId)) {
						Respond(ctx.Response, 400, "{\"error\":\"invalid entity id\"}");
						return;
					}
					string typeName = Uri.UnescapeDataString(parts[4]);
					string body = ReadBody(ctx.Request);
					using var doc = JsonDocument.Parse(body);
					World world = _worldAccessor();
					bool anyUpdated = false;
					foreach (var prop in doc.RootElement.EnumerateObject()) {
						string raw = prop.Value.ToString();
						if (_observer.TrySetField(world, entityId, typeName, prop.Name, raw)) {
							anyUpdated = true;
						}
					}
					if (!anyUpdated) {
						Respond(ctx.Response, 404, "{\"error\":\"entity or component or field not found\"}");
						return;
					}
					Respond(ctx.Response, 200, "{}");
					return;
				}
			}
			Respond(ctx.Response, 404, "{\"error\":\"not found\"}");
		}

		static void ServeEmbedded(HttpListenerResponse resp, string resourceName, string contentType) {
			var asm = typeof(ViewerServer).Assembly;
			using Stream? stream = asm.GetManifestResourceStream(resourceName);
			if (stream == null) {
				Respond(resp, 404, "resource not found");
				return;
			}
			byte[] bytes = new byte[stream.Length];
			stream.Read(bytes, 0, bytes.Length);
			resp.ContentType = contentType;
			resp.ContentLength64 = bytes.Length;
			resp.OutputStream.Write(bytes, 0, bytes.Length);
			resp.OutputStream.Close();
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
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}
	}

	// Serializes EntityRefValue as { "__entityRef": id }
	class EntityRefValueConverter : JsonConverter<EntityRefValue> {
		public override EntityRefValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			throw new NotSupportedException();
		}
		public override void Write(Utf8JsonWriter writer, EntityRefValue value, JsonSerializerOptions options) {
			writer.WriteStartObject();
			writer.WriteNumber("__entityRef", value.EntityId);
			writer.WriteEndObject();
		}
	}
}
