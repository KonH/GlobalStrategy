using GS.Main;
using GS.Configs;
using Newtonsoft.Json;
using UnityEngine;

namespace GS.Unity.Save {
	public class SettingsStorage {
		const string SettingsPath = "settings.json";

		class SettingsData {
			public string Locale = "";
		}

		readonly IWritableConfigSource<SettingsData> _settingsSource;
		SettingsData _data = new SettingsData();

		public SettingsStorage(IPersistentStorage storage) {
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

		public void LoadSettings() {
			try {
				_data = _settingsSource.Load();
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
