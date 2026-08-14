using System.Collections.Generic;
using GS.Game.WebClient.Services;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class FakePreferencesStore : IPreferencesStore {
		public Dictionary<string, string> Values = new();

		public string? GetItem(string key) => Values.TryGetValue(key, out var value) ? value : null;

		public void SetItem(string key, string value) => Values[key] = value;

		public void RemoveItem(string key) => Values.Remove(key);
	}

	public class AppPreferencesTests {
		[Fact]
		public void Defaults_WhenStoreEmpty_MatchGameSettings() {
			var preferences = new AppPreferences(new FakePreferencesStore());

			Assert.Equal("en", preferences.Locale);
			Assert.Equal("monthly", preferences.AutoSaveInterval);
			Assert.True(preferences.TutorialsEnabled);
			Assert.Empty(preferences.CompletedTutorialIds);
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

		[Fact]
		public void SetTutorialsEnabled_PersistsAndIsReadBack() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);

			preferences.SetTutorialsEnabled(false);

			Assert.False(preferences.TutorialsEnabled);
			Assert.Equal("false", store.Values["gs.preferences.tutorialsEnabled"]);

			preferences.SetTutorialsEnabled(true);

			Assert.True(preferences.TutorialsEnabled);
			Assert.Equal("true", store.Values["gs.preferences.tutorialsEnabled"]);
		}

		[Fact]
		public void MarkTutorialCompleted_PersistsIdsWithoutDuplicates() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);

			preferences.MarkTutorialCompleted("tutorial_0");
			preferences.MarkTutorialCompleted("tutorial_1");
			preferences.MarkTutorialCompleted("tutorial_0");

			Assert.Equal(new[] { "tutorial_0", "tutorial_1" }, preferences.CompletedTutorialIds);
			Assert.Contains("tutorial_0", store.Values["gs.preferences.completedTutorials"]);
			Assert.Contains("tutorial_1", store.Values["gs.preferences.completedTutorials"]);
		}

		[Fact]
		public void ClearCompletedTutorials_EmptiesPersistedIds() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);
			preferences.MarkTutorialCompleted("tutorial_0");

			preferences.ClearCompletedTutorials();

			Assert.Empty(preferences.CompletedTutorialIds);
			Assert.Equal("[]", store.Values["gs.preferences.completedTutorials"]);
		}

		[Fact]
		public void ResetToDefaults_RemovesKnownKeysAndRestoresDefaults() {
			var store = new FakePreferencesStore();
			var preferences = new AppPreferences(store);
			preferences.SetLocale("ru");
			preferences.SetAutoSaveInterval("yearly");
			preferences.SetTutorialsEnabled(false);
			preferences.MarkTutorialCompleted("tutorial_0");

			preferences.ResetToDefaults();

			Assert.Empty(store.Values);
			Assert.Equal("en", preferences.Locale);
			Assert.Equal("monthly", preferences.AutoSaveInterval);
			Assert.True(preferences.TutorialsEnabled);
			Assert.Empty(preferences.CompletedTutorialIds);
		}
	}
}
