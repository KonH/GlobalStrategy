using GS.Game.Systems;

namespace GS.Game.WebClient.Services {
	public sealed class AppPreferencesTutorialProgressSink : ITutorialProgressSink {
		readonly AppPreferences _preferences;

		public AppPreferencesTutorialProgressSink(AppPreferences preferences) {
			_preferences = preferences;
		}

		public void MarkCompleted(string taskId) {
			_preferences.MarkTutorialCompleted(taskId);
		}
	}
}
