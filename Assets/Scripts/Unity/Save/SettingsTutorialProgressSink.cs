using GS.Game.Systems;

namespace GS.Unity.Save {
	public sealed class SettingsTutorialProgressSink : ITutorialProgressSink {
		readonly SettingsStorage _settings;

		public SettingsTutorialProgressSink(SettingsStorage settings) {
			_settings = settings;
		}

		public void MarkCompleted(string taskId) {
			_settings.MarkTutorialCompleted(taskId);
		}
	}
}
