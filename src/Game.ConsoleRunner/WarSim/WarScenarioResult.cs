namespace GS.Game.ConsoleRunner.WarSim {
	public class WarScenarioResult {
		public bool Resolved;
		public int DurationDays;
		public int BattleCount;
		// Peak count of each side's own provinces occupied by the enemy during the war.
		public int AttackerProvincesOccupiedPeak;
		public int DefenderProvincesOccupiedPeak;
		public double AttackerLosses;
		public double DefenderLosses;
		// Losses as a percentage of the side's initial recruits pool (population * RecruitsInitialPercent%
		// at war start) — a fixed reference point that doesn't shift as recruits regrow mid-war.
		public double AttackerLossPercent;
		public double DefenderLossPercent;
		// The eventual loser's peak occupied-province count as a percentage of that side's total
		// province count — how much of the losing side actually got overrun.
		public double LoserProvincesOccupiedPercent;
		// "Attacker" / "Defender" / "Unresolved"
		public string Winner = "";
		public double FinalProgress;
		// Whether the side that won the chronologically first battle of the war also went on to win
		// the war overall — null when unresolved, or the first battle's winner can't be determined.
		// Directly measures the "one grand battle decides everything" failure mode.
		public bool? FirstBattleWinnerWonWar;
	}
}
