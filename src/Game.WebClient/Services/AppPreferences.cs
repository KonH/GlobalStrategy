namespace GS.Game.WebClient.Services {
	public class AppPreferences {
		public const string DefaultLocale = "en";
		public const string DefaultAutoSaveInterval = "monthly";

		const string LocaleKey = "gs.preferences.locale";
		const string AutoSaveIntervalKey = "gs.preferences.autoSaveInterval";

		readonly IPreferencesStore _store;

		public AppPreferences(IPreferencesStore store) {
			_store = store;
		}

		public string Locale => _store.GetItem(LocaleKey) ?? DefaultLocale;

		public string AutoSaveInterval => _store.GetItem(AutoSaveIntervalKey) ?? DefaultAutoSaveInterval;

		public void SetLocale(string locale) => _store.SetItem(LocaleKey, locale);

		public void SetAutoSaveInterval(string interval) => _store.SetItem(AutoSaveIntervalKey, interval);
	}
}
