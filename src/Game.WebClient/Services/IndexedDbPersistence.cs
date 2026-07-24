using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace GS.Game.WebClient.Services {
	// IStoragePersistence backed by IndexedDB via wwwroot/js/storage.js. IndexedDB
	// access is inherently async, so this uses IJSRuntime (not IJSInProcessRuntime).
	public class IndexedDbPersistence : IStoragePersistence {
		readonly IJSRuntime _jsRuntime;

		public IndexedDbPersistence(IJSRuntime jsRuntime) {
			_jsRuntime = jsRuntime;
		}

		public async Task<IReadOnlyDictionary<string, string>> LoadAllAsync(string rootDir) {
			var entries = await _jsRuntime.InvokeAsync<StorageEntry[]>("gameStorage.loadAll", rootDir);
			var result = new Dictionary<string, string>(entries.Length);
			foreach (var entry in entries) {
				result[entry.Key] = entry.Value;
			}
			return result;
		}

		public async Task PersistAsync(string relativePath, string? content) {
			if (content == null) {
				await _jsRuntime.InvokeVoidAsync("gameStorage.delete", relativePath);
			} else {
				await _jsRuntime.InvokeVoidAsync("gameStorage.put", relativePath, content);
			}
		}

		// Shape returned by js/storage.js's loadAll(prefix); property names must match
		// the JS object's keys (System.Text.Json's default JS interop is case-insensitive).
		public class StorageEntry {
			public string Key { get; set; } = "";
			public string Value { get; set; } = "";
		}
	}
}
