using System.Collections.Generic;

namespace GS.Game.ConsoleRunner {
	public class SimulationParameters {
		public List<string> OrgIds { get; set; } = new();
		public string ConfigDir { get; set; } = "";
		public int HoursPerTick { get; set; }
		public double DeltaTime { get; set; }
		public string? EndDate { get; set; }
		public int? MaxTicks { get; set; }
		public int TimeoutSeconds { get; set; }
	}

	public class OrgMetricsResult {
		public string OrgId { get; set; } = "";
		public int TotalControl { get; set; }
		public double Gold { get; set; }
	}

	public class TimelineSample {
		public string Date { get; set; } = "";
		public List<OrgMetricsResult> Orgs { get; set; } = new();
	}

	public class SimulationResult {
		public int Seed { get; set; }
		public SimulationParameters Parameters { get; set; } = new();
		public int TickCount { get; set; }
		public string EndReason { get; set; } = "";
		public string FinalDate { get; set; } = "";
		public List<OrgMetricsResult> Orgs { get; set; } = new();
		public List<TimelineSample> Timeline { get; set; } = new();
	}
}
