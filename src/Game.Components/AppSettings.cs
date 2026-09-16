using GS.Game.Common;

namespace GS.Game.Components {
	public enum AutoSaveInterval { Daily, Monthly, Yearly }

	[Savable]
	public struct AppSettings {
		[LocaleId] public string Locale;
		public AutoSaveInterval AutoSaveInterval;
	}
}
