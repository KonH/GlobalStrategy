using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using ECS.Viewer;
using ECS.Viewer.Host;
using ECS.Viewer.Server;
using GS.Game.Common;
using GS.Game.Components;
using Xunit;

namespace ECS.Viewer.Tests {
	public class ServerEndpointTests : IDisposable {
		readonly World _world;
		readonly PauseToken _pauseToken;
		readonly SimulationMarshal _marshal;
		readonly ViewerServer _server;
		readonly HttpClient _http;
		readonly int _entityId;
		readonly CancellationTokenSource _drainCts;
		readonly string _staticRoot;

		struct Hp { public int Value; }

		public ServerEndpointTests() {
			_world = new World();
			_entityId = _world.Create();
			_world.Add(_entityId, new Hp { Value = 100 });

			_pauseToken = new PauseToken();
			_marshal = new SimulationMarshal();
			_staticRoot = Path.Combine(Path.GetTempPath(), "ecs-viewer-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_staticRoot);
			Directory.CreateDirectory(Path.Combine(_staticRoot, "_framework"));
			File.WriteAllText(Path.Combine(_staticRoot, "index.html"), "<html>blazor-shell</html>");
			File.WriteAllText(Path.Combine(_staticRoot, "_framework", "blazor.boot.json"), "{}");

			var observer = new WorldObserver();
			var handler = new ViewerRequestHandler(
				_staticRoot,
				_marshal,
				_pauseToken,
				observer,
				() => _world);
			_server = new ViewerServer(handler);
			_server.Start();

			_drainCts = new CancellationTokenSource();
			Task.Run(() => {
				while (!_drainCts.IsCancellationRequested) {
					_marshal.Drain();
					Thread.Sleep(1);
				}
			});
			Task.Delay(80).Wait();

			_http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_server.Port}/") };
		}

		public void Dispose() {
			_drainCts.Cancel();
			_server.Stop();
			_http.Dispose();
			try {
				Directory.Delete(_staticRoot, recursive: true);
			} catch {
			}
		}

		[Fact]
		public async Task GetSnapshot_ReturnsFieldsAsList() {
			var resp = await _http.GetAsync("/snapshot");
			resp.EnsureSuccessStatusCode();
			string json = await resp.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			Assert.True(doc.RootElement.TryGetProperty("Entities", out JsonElement entities));
			Assert.True(entities.GetArrayLength() >= 1);
			JsonElement fields = entities[0].GetProperty("Components")[0].GetProperty("Fields");
			Assert.Equal(JsonValueKind.Array, fields.ValueKind);
		}

		[Fact]
		public async Task GetHud_ReturnsTimeAndCompletionShape() {
			var resp = await _http.GetAsync("/hud");
			resp.EnsureSuccessStatusCode();
			string json = await resp.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			Assert.True(doc.RootElement.TryGetProperty("CurrentTime", out _));
			Assert.True(doc.RootElement.TryGetProperty("IsPaused", out _));
			Assert.True(doc.RootElement.TryGetProperty("MultiplierIndex", out _));
			Assert.True(doc.RootElement.TryGetProperty("Entries", out _));
			Assert.True(doc.RootElement.TryGetProperty("IsCompleted", out _));
			Assert.True(doc.RootElement.TryGetProperty("WinnerOrganizationId", out _));
			Assert.True(doc.RootElement.TryGetProperty("Result", out _));
		}

		[Fact]
		public async Task PostPause_SetsPauseToken() {
			var content = new StringContent("{\"paused\":true}", Encoding.UTF8, "application/json");
			await _http.PostAsync("/pause", content);

			Assert.True(_pauseToken.IsPaused);

			var getResp = await _http.GetAsync("/pause");
			string json = await getResp.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			Assert.True(doc.RootElement.GetProperty("paused").GetBoolean());
		}

		[Fact]
		public async Task PatchField_ValidPayload_Returns200() {
			var content = new StringContent("{\"Value\":\"42\"}", Encoding.UTF8, "application/json");
			var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"/entity/{_entityId}/component/Hp") {
				Content = content
			};
			var resp = await _http.SendAsync(req);
			Assert.Equal(200, (int)resp.StatusCode);
		}

		[Fact]
		public async Task PatchField_UnknownEntity_Returns404() {
			var content = new StringContent("{\"Value\":\"1\"}", Encoding.UTF8, "application/json");
			var req = new HttpRequestMessage(new HttpMethod("PATCH"), "/entity/999999/component/Hp") {
				Content = content
			};
			var resp = await _http.SendAsync(req);
			Assert.Equal(404, (int)resp.StatusCode);
		}

		[Fact]
		public async Task PatchField_UnknownType_Returns404() {
			var content = new StringContent("{\"Value\":\"1\"}", Encoding.UTF8, "application/json");
			var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"/entity/{_entityId}/component/NoSuchType") {
				Content = content
			};
			var resp = await _http.SendAsync(req);
			Assert.Equal(404, (int)resp.StatusCode);
		}

		[Fact]
		public async Task GetRoot_ServesIndexHtml_NotJsonApi() {
			var resp = await _http.GetAsync("/");
			resp.EnsureSuccessStatusCode();
			Assert.Contains("html", resp.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
			string body = await resp.Content.ReadAsStringAsync();
			Assert.Contains("blazor-shell", body, StringComparison.Ordinal);
		}

		[Fact]
		public async Task GetExtensionlessNonApi_FallsBackToIndex_ApiRouteNotStolen() {
			var spa = await _http.GetAsync("/game");
			spa.EnsureSuccessStatusCode();
			string spaBody = await spa.Content.ReadAsStringAsync();
			Assert.Contains("blazor-shell", spaBody, StringComparison.Ordinal);

			var hud = await _http.GetAsync("/hud");
			hud.EnsureSuccessStatusCode();
			Assert.Equal("application/json", hud.Content.Headers.ContentType?.MediaType);
		}

		[Fact]
		public async Task GetIds_WithoutLogic_ReturnsEmptyObject() {
			var resp = await _http.GetAsync("/ids");
			resp.EnsureSuccessStatusCode();
			string json = await resp.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
		}

		[Fact]
		public void ServeStatic_RejectsSiblingPrefixPath() {
			var observer = new WorldObserver();
			var handler = new ViewerRequestHandler(
				_staticRoot,
				_marshal,
				_pauseToken,
				observer,
				() => _world);
			string evil = Path.GetFullPath(_staticRoot) + "-evil";
			Directory.CreateDirectory(evil);
			File.WriteAllText(Path.Combine(evil, "secret.txt"), "nope");
			string relative = Path.GetFileName(evil) + "/secret.txt";
			ViewerHttpResult result = handler.Handle("GET", "/../" + relative.Replace('\\', '/'), "", "");
			Assert.True(result.StatusCode == 400 || result.StatusCode == 404);
		}

		[Fact]
		public void Capture_StampsDomainIdKind_AndOmitsAttributedFields() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new Resource { ResourceId = "gold", Value = 3 });
			world.Add(entity, new ProximityMapData { Distances = new System.Collections.Generic.Dictionary<(string, string), float>() });

			var observer = new WorldObserver();
			WorldSnapshot snap = observer.Capture(world, SnapshotFieldMetadata.Apply);
			EntitySnapshot es = snap.Entities[0];
			ComponentSnapshot resource = es.Components.Find(c => c.TypeName == nameof(Resource))!;
			FieldSnapshot idField = resource.Fields.Find(f => f.Name == nameof(Resource.ResourceId))!;
			Assert.Equal("ResourceId", idField.DomainIdKind);

			ComponentSnapshot proximity = es.Components.Find(c => c.TypeName == nameof(ProximityMapData))!;
			Assert.NotNull(proximity);
			Assert.DoesNotContain(proximity.Fields, f => f.Name == nameof(ProximityMapData.Distances));
		}
	}
}
