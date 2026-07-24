using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GS.Main;

namespace GS.Game.WebClient.Services {
	// GS.Main.IPersistentStorage is a fully synchronous API (SaveFileManager and
	// GameLogic call Write/Read/Exists/Delete/List synchronously), but the real
	// persistence (IndexedDB via JS interop) is only ever async. BrowserStorage bridges
	// the two: a synchronous in-memory cache, keyed by full relative path (e.g.
	// "Saves/foo.json"), preloaded once at app startup via InitializeAsync() and kept
	// warm afterwards by write-behind persisting every mutation through
	// IStoragePersistence.
	public class BrowserStorage : IPersistentStorage {
		readonly IStoragePersistence _persistence;
		readonly IGameLogger _logger;
		readonly Dictionary<string, string> _cache = new();
		bool _initialized;

		public BrowserStorage(IStoragePersistence persistence, IGameLogger logger) {
			_persistence = persistence;
			_logger = logger;
		}

		// Must be awaited exactly once at app startup, before any game code touches
		// this storage - the synchronous methods below fail fast if called too early.
		public async Task InitializeAsync() {
			var loaded = await _persistence.LoadAllAsync("Saves");
			_cache.Clear();
			foreach (var entry in loaded) {
				_cache[entry.Key] = entry.Value;
			}
			_initialized = true;
		}

		public void Write(string relativePath, string content) {
			EnsureInitialized();
			_cache[relativePath] = content;
			QueuePersist(relativePath, content);
		}

		public string Read(string relativePath) {
			EnsureInitialized();
			if (!_cache.TryGetValue(relativePath, out string? content)) {
				throw new InvalidOperationException(
					$"BrowserStorage.Read: no cached entry for '{relativePath}'.");
			}
			return content;
		}

		public bool Exists(string relativePath) {
			EnsureInitialized();
			return _cache.ContainsKey(relativePath);
		}

		public void Delete(string relativePath) {
			EnsureInitialized();
			_cache.Remove(relativePath);
			QueuePersist(relativePath, null);
		}

		public IReadOnlyList<string> List(string relativeDir) {
			EnsureInitialized();
			string prefix = relativeDir.TrimEnd('/') + "/";
			var result = new List<string>();
			foreach (var key in _cache.Keys) {
				if (!key.StartsWith(prefix, StringComparison.Ordinal)) {
					continue;
				}
				string fileName = key.Substring(prefix.Length);
				if (!fileName.Contains('/')) {
					result.Add(fileName);
				}
			}
			return result;
		}

		// Write-behind: the cache mutation above is already visible synchronously;
		// this fire-and-forget task carries the same change to IndexedDB without
		// blocking IPersistentStorage's synchronous signature.
		void QueuePersist(string relativePath, string? content) {
			_ = PersistAsync(relativePath, content);
		}

		async Task PersistAsync(string relativePath, string? content) {
			try {
				await _persistence.PersistAsync(relativePath, content);
			} catch (Exception ex) {
				_logger.LogError($"BrowserStorage: failed to persist '{relativePath}': {ex.Message}");
			}
		}

		void EnsureInitialized() {
			if (!_initialized) {
				throw new InvalidOperationException(
					"BrowserStorage was used before InitializeAsync() completed - the cache must be warmed at app startup.");
			}
		}
	}
}
