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
		public double AttackerWarProgressDecayPerMonth { get; set; } = 2.5;
		public double RevengeDamageBonusDecayPerMonth { get; set; } = 1.0;
		public double RevengeDurabilityBonusDecayPerMonth { get; set; } = 0.5;
		public double PeaceMinLoseBand { get; set; } = 20;
		public double PeaceMinWinBand { get; set; } = 20;
		// Rolled once per day (Wars.TryResolvePeaceByChance): a low daily floor keeps cumulative
		// odds over a month close to 1% at the band edge, while +/-100 progress resolves same-day.
		public double PeaceChanceMinPercent { get; set; } = 0.033;
		public double PeaceChanceMaxPercent { get; set; } = 100;
		public double PeaceProvinceTransferMinPercent { get; set; } = 10;
		public double PeaceProvinceTransferMaxPercent { get; set; } = 30;
		public double PeaceGoldPerMonth { get; set; } = 1000;
		public double PeaceWinnerControlIncreaseFraction { get; set; } = 0.5;
		public double PeaceLoserControlDecreaseFraction { get; set; } = 1.0;
		public double CardCooldownDays { get; set; } = 7;
		public WarBattleSettings WarBattles { get; set; } = new WarBattleSettings();
		public string[] ResourceIdUpdateOrder { get; set; } = {
			ResourceDefinitions.Population, ResourceDefinitions.CountryPopulation, ResourceDefinitions.CountryScore,
			ResourceDefinitions.Recruits, ResourceDefinitions.OrgScore,
			ResourceDefinitions.Damage, ResourceDefinitions.Durability
		};
		public int BotActionLogRetentionCap { get; set; } = 500;
		public int MaxControlPool { get; set; } = 100;
		public CompletionConditionConfig CompletionCondition { get; set; } = new CompletionConditionConfig {
			Type = "any",
			Members = new List<CompletionConditionConfig> {
				new CompletionConditionConfig { Type = "total_control", Value = 0.8 },
				new CompletionConditionConfig { Type = "full_control_countries", Value = 15 },
				new CompletionConditionConfig { Type = "score_goal", Value = 275592 }
			}
		};
		public GameLogSettings GameLog { get; set; } = new GameLogSettings();
		public EventNotificationSettings EventNotifications { get; set; } = new();
		public FeatureFlagSettings FeatureFlags { get; set; } = new();
		public List<EndGameComparisonEntry> EndGameComparisons { get; set; } = new List<EndGameComparisonEntry>();

		public List<BotFeatureConfigEntry> BotFeatures { get; set; } = new List<BotFeatureConfigEntry> {
			new BotFeatureConfigEntry {
				FeatureId = "control",
				Enabled = true,
				Parameters = new Dictionary<string, double>()
			}
		};
	}

	public class WarBattleSettings {
		public int BaseConcurrentBattleCount { get; set; } = 1;
		public int SharedBorderPairsPerAdditionalBattle { get; set; } = 5;
		public double AttackerInitialInitiative { get; set; } = 1.0;
		public double DefenderInitialInitiative { get; set; } = 0.0;
		public double InitiationCost { get; set; } = 1.0;
		public double RoundWinnerInitiativeGain { get; set; } = 0.5;
		public double BattleProgressGain { get; set; } = 10;
		public int FallbackCandidateCount { get; set; } = 3;
		public double TroopDenominatorOffset { get; set; } = 1;
		public double TroopRandomMin { get; set; } = 0.9;
		public double TroopRandomMax { get; set; } = 1.1;
		public double RoundIntervalHours { get; set; } = 1;
		public double DamageDivisor { get; set; } = 300;
		public double DurabilityDivisor { get; set; } = 300;
		public double CasualtyRandomMin { get; set; } = 0.9;
		public double CasualtyRandomMax { get; set; } = 1.1;
		public double MinimumCasualtyFraction { get; set; } = 0.01;
		public double MinimumAbsoluteCasualties { get; set; } = 1;
		public int MaxFinishedBattlesRetained { get; set; } = 30;

		public void Validate() {
			if (BaseConcurrentBattleCount <= 0) {
				throw new System.InvalidOperationException("WarBattles.BaseConcurrentBattleCount must be positive.");
			}
			if (SharedBorderPairsPerAdditionalBattle <= 0) {
				throw new System.InvalidOperationException("WarBattles.SharedBorderPairsPerAdditionalBattle must be positive.");
			}
			if (FallbackCandidateCount <= 0) {
				throw new System.InvalidOperationException("WarBattles.FallbackCandidateCount must be positive.");
			}
			if (TroopDenominatorOffset <= 0) {
				throw new System.InvalidOperationException("WarBattles.TroopDenominatorOffset must be positive.");
			}
			if (RoundIntervalHours <= 0 || DamageDivisor <= 0 || DurabilityDivisor <= 0) {
				throw new System.InvalidOperationException("War battle intervals and combat divisors must be positive.");
			}
			if (TroopRandomMin <= 0 || TroopRandomMin > TroopRandomMax) {
				throw new System.InvalidOperationException("War battle troop random bounds must be positive and ordered.");
			}
			if (CasualtyRandomMin <= 0 || CasualtyRandomMin > CasualtyRandomMax) {
				throw new System.InvalidOperationException("War battle casualty random bounds must be positive and ordered.");
			}
			if (MinimumCasualtyFraction < 0 || MinimumAbsoluteCasualties < 0) {
				throw new System.InvalidOperationException("War battle casualty minimums cannot be negative.");
			}
			if (MaxFinishedBattlesRetained <= 0) {
				throw new System.InvalidOperationException("WarBattles.MaxFinishedBattlesRetained must be positive.");
			}
		}
	}

	public class BotFeatureConfigEntry {
		public string FeatureId { get; set; } = "";
		public bool Enabled { get; set; } = true;
		public Dictionary<string, double> Parameters { get; set; } = new Dictionary<string, double>();
	}

	public class EndGameComparisonEntry {
		public string ComparisonElementId { get; set; } = "";
		public double Score { get; set; }
	}
}
