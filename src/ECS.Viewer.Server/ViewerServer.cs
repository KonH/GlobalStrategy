using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ECS.Viewer.Host;

namespace ECS.Viewer.Server {
	public class ViewerServer {
		readonly ViewerRequestHandler _handler;
		HttpListener? _listener;
		public int Port { get; private set; }

		public ViewerServer(ViewerRequestHandler handler) {
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
		}

		public void Start() {
			Port = FindFreePort();
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{Port}/");
			_listener.Start();
			Console.WriteLine($"[ECS Viewer] http://localhost:{Port}?host=remote");
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
				string method = ctx.Request.HttpMethod;
				string path = ctx.Request.Url?.AbsolutePath ?? "/";
				string query = ctx.Request.Url?.Query ?? "";
				string body = ReadBody(ctx.Request);
				ViewerHttpResult result = _handler.Handle(method, path, query, body);
				Write(ctx.Response, result);
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

		static void Write(HttpListenerResponse resp, ViewerHttpResult result) {
			resp.StatusCode = result.StatusCode;
			resp.ContentType = result.ContentType;
			resp.Headers["Access-Control-Allow-Origin"] = "*";
			resp.Headers["Access-Control-Allow-Methods"] = "GET, POST, PATCH, OPTIONS";
			resp.Headers["Access-Control-Allow-Headers"] = "Content-Type";
			resp.ContentLength64 = result.Body.Length;
			resp.OutputStream.Write(result.Body, 0, result.Body.Length);
			resp.OutputStream.Close();
		}

		static string ReadBody(HttpListenerRequest req) {
			using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? System.Text.Encoding.UTF8);
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
}
