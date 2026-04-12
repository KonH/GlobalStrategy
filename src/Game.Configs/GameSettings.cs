namespace GS.Game.Configs {
	public class GameSettings {
		public int StartYear { get; set; } = 1880;
		public int[] SpeedMultipliers { get; set; } = { 1, 24, 720 };
		public string DefaultLocale { get; set; } = "en";
		public string AutoSaveInterval { get; set; } = "monthly";
	}
}
