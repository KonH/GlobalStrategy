using GS.Unity.Save;
using UnityEngine;

namespace GS.Unity.UI {
	public class CustomLocalization : ILocalization {
		readonly LocalizationConfig _config;
		readonly SettingsStorage _settings;
		LocaleConfig _active;

		public CustomLocalization(LocalizationConfig config, SettingsStorage settings) {
			_config = config;
			_settings = settings;
			string savedLocale = string.IsNullOrEmpty(settings.Locale) ? config.DefaultLocale : settings.Locale;
			_active = FindLocale(savedLocale) ?? FindLocale(config.DefaultLocale);
			if (_active == null) {
				Debug.LogWarning($"[Localization] No locale found for default '{config.DefaultLocale}' (available: {config.Locales?.Length ?? 0})");
			} else {
				Debug.Log($"[Localization] Loaded locale '{_active.Locale}' with {_active.Entries?.Length ?? 0} entries");
			}
		}

		public string CurrentLocale => _active?.Locale ?? _config.DefaultLocale;

		public string Get(string key) {
			if (_active != null) {
				foreach (var e in _active.Entries) {
					if (e.Key == key) {
						return e.Value;
					}
				}
			}
			Debug.LogWarning($"[Localization] Key not found: '{key}' (locale: {_active?.Locale ?? "null"})");
			return key;
		}

		public void SetLocale(string locale) {
			var found = FindLocale(locale);
			if (found == null) {
				Debug.LogWarning($"[Localization] SetLocale: no locale found for '{locale}'");
				return;
			}
			_active = found;
			_settings.Locale = locale;
			Debug.Log($"[Localization] Switched to locale '{locale}'");
		}

		LocaleConfig FindLocale(string locale) {
			foreach (var l in _config.Locales) {
				if (l.Locale == locale) {
					return l;
				}
			}
			return null;
		}
	}
}
