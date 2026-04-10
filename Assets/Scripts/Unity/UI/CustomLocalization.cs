namespace GS.Unity.UI {
	public class CustomLocalization : ILocalization {
		readonly LocalizationConfig _config;
		LocaleConfig _active;

		public CustomLocalization(LocalizationConfig config) {
			_config = config;
			_active = FindLocale(config.DefaultLocale);
		}

		public string Get(string key) {
			if (_active != null) {
				foreach (var e in _active.Entries) {
					if (e.Key == key) {
						return e.Value;
					}
				}
			}
			return key;
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
