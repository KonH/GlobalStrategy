namespace GS.Game.ConsoleRunner {
	public class CalibrationResult {
		public string Scenario { get; set; } = "";
		public string OrgId { get; set; } = "";
		public string WinnerOrgId { get; set; } = "";
		public int Seed { get; set; }
		public bool Completed { get; set; }
		public int TickCount { get; set; }
		public string FinalDate { get; set; } = "";
		public double Score { get; set; }
	}
}
