using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ECS;
using ECS.Viewer;
using ECS.Viewer.Server;
using Xunit;

namespace ECS.Viewer.Tests {
	public class ServerEndpointTests : IDisposable {
		readonly World _world;
		readonly PauseToken _pauseToken;
		readonly ViewerServer _server;
		readonly HttpClient _http;
		readonly int _entityId;

		struct Hp { public int Value; }

		public ServerEndpointTests() {
			_world = new World();
			_entityId = _world.Create();
			_world.Add(_entityId, new Hp { Value = 100 });

			_pauseToken = new PauseToken();
			var observer = new WorldObserver();
			_server = new ViewerServer(observer, _pauseToken, () => _world);
			_server.Start();
			Task.Delay(80).Wait();

			_http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_server.Port}/") };
		}

		public void Dispose() {
			_server.Stop();
			_http.Dispose();
		}

		[Fact]
		public async Task GetSnapshot_ReturnsValidJson() {
			var resp = await _http.GetAsync("/snapshot");
			resp.EnsureSuccessStatusCode();
			string json = await resp.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			Assert.True(doc.RootElement.TryGetProperty("Entities", out _));
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
	}
}
