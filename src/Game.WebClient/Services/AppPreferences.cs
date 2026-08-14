using System.Collections.Generic;
using System.Text.Json;

namespace GS.Game.WebClient.Services {
	public class AppPreferences {
		public const string DefaultLocale = "en";
		public const string DefaultAutoSaveInterval = "monthly";
		public const bool DefaultTutorialsEnabled = true;

		const string LocaleKey = "gs.preferences.locale";
		const string AutoSaveIntervalKey = "gs.preferences.autoSaveInterval";
		const string TutorialsEnabledKey = "gs.preferences.tutorialsEnabled";
		const string CompletedTutorialsKey = "gs.preferences.completedTutorials";

		static readonly string[] KnownKeys = {
			LocaleKey,
			AutoSaveIntervalKey,
			TutorialsEnabledKey,
			CompletedTutorialsKey,
		};

		readonly IPreferencesStore _store;

		public AppPreferences(IPreferencesStore store) {
			_store = store;
		}

		public string Locale => _store.GetItem(LocaleKey) ?? DefaultLocale;

		public string AutoSaveInterval => _store.GetItem(AutoSaveIntervalKey) ?? DefaultAutoSaveInterval;

		public bool TutorialsEnabled {
			get {
				string? raw = _store.GetItem(TutorialsEnabledKey);
				if (raw == null) {
					return DefaultTutorialsEnabled;
				}
				return raw != "false";
			}
		}

		public IReadOnlyList<string> CompletedTutorialIds {
			get {
				string? raw = _store.GetItem(CompletedTutorialsKey);
				if (string.IsNullOrEmpty(raw)) {
					return System.Array.Empty<string>();
				}
				try {
					return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
				} catch (JsonException) {
					return System.Array.Empty<string>();
				}
			}
		}

		public void SetLocale(string locale) => _store.SetItem(LocaleKey, locale);

		public void SetAutoSaveInterval(string interval) => _store.SetItem(AutoSaveIntervalKey, interval);

		public void SetTutorialsEnabled(bool enabled) =>
			_store.SetItem(TutorialsEnabledKey, enabled ? "true" : "false");

		public void MarkTutorialCompleted(string taskId) {
			if (string.IsNullOrEmpty(taskId)) {
				return;
			}
			var ids = new List<string>(CompletedTutorialIds);
			if (ids.Contains(taskId)) {
				return;
			}
			ids.Add(taskId);
			_store.SetItem(CompletedTutorialsKey, JsonSerializer.Serialize(ids));
		}

		public void ClearCompletedTutorials() {
			_store.SetItem(CompletedTutorialsKey, "[]");
		}

		public void ResetToDefaults() {
			foreach (string key in KnownKeys) {
				_store.RemoveItem(key);
			}
		}
	}
}
