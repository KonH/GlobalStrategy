using System.Collections.Generic;

namespace GS.Game.Configs {
	public class GameSettings {
		public int StartYear { get; set; } = 1880;
		public int[] SpeedMultipliers { get; set; } = { 1, 24, 720 };
		public string DefaultLocale { get; set; } = "en";
		public string AutoSaveInterval { get; set; } = "monthly";
		public double PopulationGrowthPercentPerMonth { get; set; } = 0.075;
		public double CountryScoreCoefficient { get; set; } = 1.0;
		public double RecruitsInitialPercent { get; set; } = 5.0;
		public double RecruitsCapPercent { get; set; } = 15.0;
		public double RecruitsMonthlyIncreasePercent { get; set; } = 1.0;
		public string[] ResourceIdUpdateOrder { get; set; } = {
			ResourceDefinitions.Population, ResourceDefinitions.CountryPopulation, ResourceDefinitions.CountryScore,
			ResourceDefinitions.Recruits, ResourceDefinitions.OrgScore
		};
		public int BotActionLogRetentionCap { get; set; } = 500;
		public int MaxControlPool { get; set; } = 100;
		public GameLogSettings GameLog { get; set; } = new GameLogSettings();

		// discoveredCountriesAvailableControl: 0 is the eval-validated threshold (see
		// Docs/BotFeatures/discoverAndControl/eval_summary.md) - it beats the feature's
		// raw discover-first default (double.MaxValue, applied when a profile omits the
		// parameter entirely) by a wide margin.
		public List<BotFeatureConfigEntry> BotFeatures { get; set; } = new List<BotFeatureConfigEntry> {
			new BotFeatureConfigEntry {
				FeatureId = "discoverAndControl",
				Enabled = true,
				Parameters = new Dictionary<string, double> { ["discoveredCountriesAvailableControl"] = 0 }
			}
		};
	}

	public class BotFeatureConfigEntry {
		public string FeatureId { get; set; } = "";
		public bool Enabled { get; set; } = true;
		public Dictionary<string, double> Parameters { get; set; } = new Dictionary<string, double>();
	}
}
