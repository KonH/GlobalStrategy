using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GS.Game.WebClient.Services;
using GS.Main;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class BrowserStorageTests {
		sealed class FakePersistence : IStoragePersistence {
			public Dictionary<string, string> Seed = new();
			public List<(string Path, string? Content)> PersistCalls = new();

			public Task<IReadOnlyDictionary<string, string>> LoadAllAsync(string rootDir) {
				var result = new Dictionary<string, string>();
				foreach (var kvp in Seed) {
					if (kvp.Key.StartsWith(rootDir + "/", StringComparison.Ordinal)) {
						result[kvp.Key] = kvp.Value;
					}
				}
				return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
			}

			public Task PersistAsync(string relativePath, string? content) {
				PersistCalls.Add((relativePath, content));
				return Task.CompletedTask;
			}
		}

		sealed class CapturingLogger : IGameLogger {
			public List<string> Errors = new();
			public void LogError(string message) => Errors.Add(message);
			public void LogInfo(string message) { }
			public void LogDebug(string message) { }
		}

		static async Task<BrowserStorage> CreateInitializedAsync(FakePersistence persistence, IGameLogger? logger = null) {
			var storage = new BrowserStorage(persistence, logger ?? new CapturingLogger());
			await storage.InitializeAsync();
			return storage;
		}

		[Fact]
		void methods_throw_before_initialization() {
			var storage = new BrowserStorage(new FakePersistence(), new CapturingLogger());

			Assert.Throws<InvalidOperationException>(() => storage.Write("Saves/a.json", "{}"));
			Assert.Throws<InvalidOperationException>(() => storage.Read("Saves/a.json"));
			Assert.Throws<InvalidOperationException>(() => storage.Exists("Saves/a.json"));
			Assert.Throws<InvalidOperationException>(() => storage.Delete("Saves/a.json"));
			Assert.Throws<InvalidOperationException>(() => storage.List("Saves"));
		}

		[Fact]
		async Task initialize_hydrates_cache_from_persistence() {
			var persistence = new FakePersistence();
			persistence.Seed["Saves/existing.json"] = "{\"a\":1}";
			var storage = await CreateInitializedAsync(persistence);

			Assert.True(storage.Exists("Saves/existing.json"));
			Assert.Equal("{\"a\":1}", storage.Read("Saves/existing.json"));
			Assert.Equal(new[] { "existing.json" }, storage.List("Saves"));
		}

		[Fact]
		async Task write_is_immediately_visible_via_read_exists_list() {
			var persistence = new FakePersistence();
			var storage = await CreateInitializedAsync(persistence);

			storage.Write("Saves/new.json", "{\"x\":2}");

			Assert.True(storage.Exists("Saves/new.json"));
			Assert.Equal("{\"x\":2}", storage.Read("Saves/new.json"));
			Assert.Equal(new[] { "new.json" }, storage.List("Saves"));
		}

		[Fact]
		async Task write_queues_write_behind_persistence() {
			var persistence = new FakePersistence();
			var storage = await CreateInitializedAsync(persistence);

			storage.Write("Saves/new.json", "{\"x\":2}");

			Assert.Single(persistence.PersistCalls);
			Assert.Equal("Saves/new.json", persistence.PersistCalls[0].Path);
			Assert.Equal("{\"x\":2}", persistence.PersistCalls[0].Content);
		}

		[Fact]
		async Task delete_removes_from_cache_and_list_and_queues_persistence_with_null_content() {
			var persistence = new FakePersistence();
			persistence.Seed["Saves/existing.json"] = "{}";
			var storage = await CreateInitializedAsync(persistence);

			storage.Delete("Saves/existing.json");

			Assert.False(storage.Exists("Saves/existing.json"));
			Assert.Empty(storage.List("Saves"));
			Assert.Single(persistence.PersistCalls);
			Assert.Equal("Saves/existing.json", persistence.PersistCalls[0].Path);
			Assert.Null(persistence.PersistCalls[0].Content);
		}

		[Fact]
		async Task list_returns_file_names_only_not_full_paths() {
			var persistence = new FakePersistence();
			persistence.Seed["Saves/one.json"] = "{}";
			persistence.Seed["Saves/two.json"] = "{}";
			persistence.Seed["Other/three.json"] = "{}";
			var storage = await CreateInitializedAsync(persistence);

			var files = storage.List("Saves");

			Assert.Equal(2, files.Count);
			Assert.Contains("one.json", files);
			Assert.Contains("two.json", files);
		}

		[Fact]
		async Task read_of_missing_entry_throws() {
			var persistence = new FakePersistence();
			var storage = await CreateInitializedAsync(persistence);

			Assert.Throws<InvalidOperationException>(() => storage.Read("Saves/missing.json"));
		}
	}
}
