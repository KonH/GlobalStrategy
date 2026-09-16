#nullable enable
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ECS.Viewer;
using ECS.Viewer.Host;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using UnityEngine;
using VContainer;

namespace GS.Unity.EcsViewer {
	public class EcsViewerBridge : MonoBehaviour {
#if !UNITY_WEBGL || UNITY_EDITOR
		[SerializeField] bool _enabled = true;
#endif

		public static string? CurrentUrl { get; private set; }

		GameLogic _logic = null!;
		PauseToken _pauseToken = null!;
		SimulationMarshal _marshal = null!;
#if !UNITY_WEBGL || UNITY_EDITOR
		HttpListener _listener = null!;
		ViewerRequestHandler _handler = null!;
#endif

		[Inject]
		void Construct(GameLogic logic, PauseToken pauseToken, SimulationMarshal marshal) {
			_logic = logic;
			_pauseToken = pauseToken;
			_marshal = marshal;
		}

		void Start() {
#if !UNITY_WEBGL || UNITY_EDITOR
			if (!_enabled) {
				return;
			}
			string webRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".tmp", "web-debug-ui"));
			var registry = new CommandRegistry();
			var executor = new CommandExecutor(registry);
			var source = new GameLogicSuggestionSource(_logic);
			var engine = new SuggestionEngine(registry, new SuggestionValueResolver(source, new DisplayNameSuggestionLabels()));
			_handler = new ViewerRequestHandler(
				webRoot,
				_marshal,
				_pauseToken,
				new WorldObserver(),
				() => _logic.World,
				_logic,
				executor,
				engine,
				msg => Debug.LogError(msg));
			int port = FindFreePort();
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{port}/");
			_listener.Start();
			CurrentUrl = $"http://localhost:{port}?host=remote";
			Debug.Log($"[ECS Viewer] {CurrentUrl}");
			Task.Run(Loop);
#endif
		}

		void OnDestroy() {
#if !UNITY_WEBGL || UNITY_EDITOR
			_listener?.Stop();
#endif
			CurrentUrl = null;
		}

#if !UNITY_WEBGL || UNITY_EDITOR
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
				string method = ctx.Request.HttpMethod;
				string path = ctx.Request.Url?.AbsolutePath ?? "/";
				string query = ctx.Request.Url?.Query ?? "";
				string body = ReadBody(ctx.Request);
				ViewerHttpResult result = _handler.Handle(method, path, query, body);
				ctx.Response.StatusCode = result.StatusCode;
				ctx.Response.ContentType = result.ContentType;
				ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
				ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PATCH, OPTIONS";
				ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
				ctx.Response.ContentLength64 = result.Body.Length;
				ctx.Response.OutputStream.Write(result.Body, 0, result.Body.Length);
				ctx.Response.OutputStream.Close();
			} catch (Exception ex) {
				try {
					byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
					ctx.Response.StatusCode = 500;
					ctx.Response.ContentType = "application/json";
					ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
					ctx.Response.ContentLength64 = bytes.Length;
					ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
					ctx.Response.OutputStream.Close();
				} catch {
				}
			}
		}

		static string ReadBody(HttpListenerRequest req) {
			using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? System.Text.Encoding.UTF8);
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
}
