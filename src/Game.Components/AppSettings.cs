namespace GS.Game.Components {
	public enum AutoSaveInterval { Daily, Monthly, Yearly }

	[Savable]
	public struct AppSettings {
		public string Locale;
		public AutoSaveInterval AutoSaveInterval;
	}
}
