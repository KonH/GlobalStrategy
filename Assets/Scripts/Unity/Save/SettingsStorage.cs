using System.Collections.Generic;
using GS.Main;
using GS.Configs;
using Newtonsoft.Json;
using UnityEngine;

namespace GS.Unity.Save {
	public class SettingsStorage {
		const string SettingsPath = "settings.json";

		class SettingsData {
			public string Locale = "";
			public bool TutorialsEnabled = true;
			public List<string> CompletedTutorialIds = new();
		}

		readonly IPersistentStorage _storage;
		readonly IWritableConfigSource<SettingsData> _settingsSource;
		SettingsData _data = new SettingsData();

		public SettingsStorage(IPersistentStorage storage) {
			_storage = storage;
			_settingsSource = new PersistentSettingsConfigSource(storage);
			LoadSettings();
		}

		public string Locale {
			get => _data.Locale;
			set {
				_data.Locale = value;
				_settingsSource.Save(_data);
			}
		}

		public bool TutorialsEnabled {
			get => _data.TutorialsEnabled;
			set {
				_data.TutorialsEnabled = value;
				_settingsSource.Save(_data);
			}
		}

		public IReadOnlyList<string> CompletedTutorialIds => _data.CompletedTutorialIds;

		public void MarkTutorialCompleted(string taskId) {
			if (string.IsNullOrEmpty(taskId)) {
				return;
			}
			if (_data.CompletedTutorialIds.Contains(taskId)) {
				return;
			}
			_data.CompletedTutorialIds.Add(taskId);
			_settingsSource.Save(_data);
		}

		public void ClearCompletedTutorials() {
			_data.CompletedTutorialIds.Clear();
			_settingsSource.Save(_data);
		}

		public void ResetToDefaults() {
			if (_storage.Exists(SettingsPath)) {
				_storage.Delete(SettingsPath);
			}
			_data = new SettingsData();
		}

		public void LoadSettings() {
			try {
				_data = _settingsSource.Load();
				if (_data.CompletedTutorialIds == null) {
					_data.CompletedTutorialIds = new List<string>();
				}
			} catch (JsonException e) {
				Debug.LogError($"[Settings] Failed to read '{SettingsPath}': {e}");
			}
		}

		sealed class PersistentSettingsConfigSource : IWritableConfigSource<SettingsData> {
			readonly IPersistentStorage _storage;

			public PersistentSettingsConfigSource(IPersistentStorage storage) {
				_storage = storage;
			}

			public SettingsData Load() {
				if (!_storage.Exists(SettingsPath)) {
					return new SettingsData();
				}

				return JsonConvert.DeserializeObject<SettingsData>(_storage.Read(SettingsPath)) ?? new SettingsData();
			}

			public void Save(SettingsData config) {
				_storage.Write(SettingsPath, JsonConvert.SerializeObject(config));
			}
		}
	}
}
