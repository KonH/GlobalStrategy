using System.Collections.Generic;
using System.Threading.Tasks;

namespace GS.Game.WebClient.Services {
	// Async seam BrowserStorage's synchronous in-memory cache sits on top of. Kept
	// minimal so a test fake can implement it trivially (in-memory dictionary, no real
	// async work, but still Task-returning for realism).
	public interface IStoragePersistence {
		// Loads every persisted entry whose key starts with rootDir, used once at
		// startup to seed BrowserStorage's cache.
		Task<IReadOnlyDictionary<string, string>> LoadAllAsync(string rootDir);

		// Write-behind persist of a single mutation. content == null means the entry
		// was deleted.
		Task PersistAsync(string relativePath, string? content);
	}
}
