using System;
using System.Collections.Generic;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SaveFileManagerTests {
		static WorldSnapshot MakeSnapshot(string saveName, string playerCountryId, DateTime gameDate, DateTime savedAt) {
			return new WorldSnapshot {
				Header = new SaveHeader {
					SaveName = saveName,
					OrganizationId = playerCountryId,
					GameDate = gameDate,
					SavedAt = savedAt
				},
				Entities = new List<EntitySnapshot>()
			};
		}

		sealed class FakeStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new Dictionary<string, string>();
			readonly HashSet<string> _deleted = new HashSet<string>();

			public void Write(string relativePath, string content) => _files[relativePath] = content;
			public string Read(string relativePath) => _files[relativePath];
			public bool Exists(string relativePath) => _files.ContainsKey(relativePath);
			public void Delete(string relativePath) {
				_files.Remove(relativePath);
				_deleted.Add(relativePath);
			}
			public bool WasDeleted(string relativePath) => _deleted.Contains(relativePath);

			public IReadOnlyList<string> List(string relativeDir) {
				var result = new List<string>();
				string prefix = relativeDir + "/";
				foreach (var key in _files.Keys) {
					if (key.StartsWith(prefix)) {
						result.Add(key.Substring(prefix.Length));
					}
				}
				return result;
			}

			public void AddFile(string relativePath, string content) => _files[relativePath] = content;
		}

		sealed class FakeSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _snapshots = new Dictionary<string, WorldSnapshot>();

			public void Register(string json, WorldSnapshot snapshot) => _snapshots[json] = snapshot;

			public string Serialize(WorldSnapshot snapshot) {
				foreach (var kvp in _snapshots) {
					if (kvp.Value == snapshot) return kvp.Key;
				}
				return "{}";
			}

			public WorldSnapshot Deserialize(string json) => _snapshots[json];
		}

		[Fact]
		void list_saves_returns_sorted_by_saved_at_desc() {
			var storage = new FakeStorage();
			var serializer = new FakeSerializer();

			var older = MakeSnapshot("save1", "RU", new DateTime(1880, 1, 1), new DateTime(2026, 1, 1));
			var newer = MakeSnapshot("save2", "RU", new DateTime(1881, 1, 1), new DateTime(2026, 6, 1));

			serializer.Register("json1", older);
			serializer.Register("json2", newer);

			storage.AddFile("Saves/save1.json", "json1");
			storage.AddFile("Saves/save2.json", "json2");

			var manager = new SaveFileManager(storage, serializer);
			var saves = manager.ListSaves();

			Assert.Equal(2, saves.Count);
			Assert.Equal("save2", saves[0].SaveName);
			Assert.Equal("save1", saves[1].SaveName);
		}

		[Fact]
		void get_last_save_returns_most_recent() {
			var storage = new FakeStorage();
			var serializer = new FakeSerializer();

			var older = MakeSnapshot("old", "RU", new DateTime(1880, 1, 1), new DateTime(2026, 1, 1));
			var newer = MakeSnapshot("new", "RU", new DateTime(1881, 1, 1), new DateTime(2026, 6, 1));

			serializer.Register("json_old", older);
			serializer.Register("json_new", newer);

			storage.AddFile("Saves/old.json", "json_old");
			storage.AddFile("Saves/new.json", "json_new");

			var manager = new SaveFileManager(storage, serializer);
			Assert.Equal("new", manager.GetLastSave()!.SaveName);
		}

		[Fact]
		void get_last_save_returns_null_when_no_saves() {
			var storage = new FakeStorage();
			var serializer = new FakeSerializer();
			var manager = new SaveFileManager(storage, serializer);
			Assert.Null(manager.GetLastSave());
		}

		[Fact]
		void delete_save_removes_file() {
			var storage = new FakeStorage();
			var serializer = new FakeSerializer();

			var snap = MakeSnapshot("mysave", "RU", new DateTime(1880, 1, 1), new DateTime(2026, 1, 1));
			serializer.Register("json", snap);
			storage.AddFile("Saves/mysave.json", "json");

			var manager = new SaveFileManager(storage, serializer);
			manager.DeleteSave("mysave");

			Assert.True(storage.WasDeleted("Saves/mysave.json"));
		}

		[Fact]
		void list_saves_skips_corrupted_files() {
			var storage = new FakeStorage();
			var serializer = new FakeSerializer();

			var valid = MakeSnapshot("valid", "RU", new DateTime(1880, 1, 1), new DateTime(2026, 1, 1));
			serializer.Register("valid_json", valid);
			storage.AddFile("Saves/valid.json", "valid_json");
			storage.AddFile("Saves/corrupt.json", "THIS_IS_NOT_VALID_JSON");

			var manager = new SaveFileManager(storage, serializer);
			var saves = manager.ListSaves();

			Assert.Single(saves);
			Assert.Equal("valid", saves[0].SaveName);
		}
	}
}
