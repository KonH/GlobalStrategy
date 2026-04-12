using System;
using System.Collections.Generic;

namespace GS.Main {
	public class SaveFileInfo {
		public string SaveName { get; set; } = "";
		public string PlayerCountryId { get; set; } = "";
		public DateTime GameDate { get; set; }
		public DateTime SavedAt { get; set; }
	}

	public class SaveFileManager {
		readonly IPersistentStorage _storage;
		readonly ISnapshotSerializer _serializer;

		public SaveFileManager(IPersistentStorage storage, ISnapshotSerializer serializer) {
			_storage = storage;
			_serializer = serializer;
		}

		public IReadOnlyList<SaveFileInfo> ListSaves() {
			var result = new List<SaveFileInfo>();
			var files = _storage.List("Saves");
			foreach (var file in files) {
				if (!file.EndsWith(".json")) {
					continue;
				}
				try {
					string saveName = file.Substring(0, file.Length - ".json".Length);
					string json = _storage.Read($"Saves/{file}");
					var snapshot = _serializer.Deserialize(json);
					result.Add(new SaveFileInfo {
						SaveName = saveName,
						PlayerCountryId = snapshot.Header.PlayerCountryId,
						GameDate = snapshot.Header.GameDate,
						SavedAt = snapshot.Header.SavedAt
					});
				} catch {
					// skip corrupted saves
				}
			}
			result.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
			return result;
		}

		public void DeleteSave(string saveName) {
			_storage.Delete($"Saves/{saveName}.json");
		}

		public SaveFileInfo? GetLastSave() {
			var saves = ListSaves();
			return saves.Count > 0 ? saves[0] : null;
		}
	}
}
