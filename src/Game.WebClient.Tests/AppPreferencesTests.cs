using System.Collections.Generic;
using GS.Game.WebClient.Services;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class FakePreferencesStore : IPreferencesStore {
		public Dictionary<string, string> Values = new();

		public string? GetItem(string key) => Values.TryGetValue(key, out var value) ? value : null;

		public void SetItem(string key, string value) => Values[key] = value;
	}

	public class AppPreferencesTests {
		[Fact]
		public void Defaults_WhenStoreEmpty_MatchGameSettings() {
			var preferences = new AppPreferences(new FakePreferencesStore());

			Assert.Equal("en", preferences.Locale);
			Assert.Equal("monthly", preferences.AutoSaveInterval);
		}

		[Fact]
		public void SetLocale_PersistsAndIsReadBack() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);

			preferences.SetLocale("ru");

			Assert.Equal("ru", preferences.Locale);
			Assert.Equal("ru", store.Values["gs.preferences.locale"]);
		}

		[Fact]
		public void SetAutoSaveInterval_PersistsAndIsReadBack() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);

			preferences.SetAutoSaveInterval("yearly");

			Assert.Equal("yearly", preferences.AutoSaveInterval);
			Assert.Equal("yearly", store.Values["gs.preferences.autoSaveInterval"]);
		}
	}
}
