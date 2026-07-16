using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GS.Game.ConsoleRunner {
	public class SimulationParameters {
		public List<string> OrgIds { get; set; } = new();
		public string ConfigDir { get; set; } = "";
		public int HoursPerTick { get; set; }
		public double DeltaTime { get; set; }
		public string? EndDate { get; set; }
		public int? MaxTicks { get; set; }
		public int TimeoutSeconds { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<BotProfileResult>? Bots { get; set; }
	}

	public class BotProfileResult {
		public string OrgId { get; set; } = "";
		public List<BotFeatureResult> Features { get; set; } = new();
	}

	public class BotFeatureResult {
		public string FeatureId { get; set; } = "";
		public bool Enabled { get; set; }
		public Dictionary<string, double> Parameters { get; set; } = new();
	}

	public class OrgMetricsResult {
		public string OrgId { get; set; } = "";
		public int TotalControl { get; set; }
		public double Gold { get; set; }
		public double Score { get; set; }
	}

	public class TimelineSample {
		public string Date { get; set; } = "";
		public List<OrgMetricsResult> Orgs { get; set; } = new();
	}

	public class BotEmission {
		public string FeatureId { get; set; } = "";
		public string ActionId { get; set; } = "";
		public string CountryId { get; set; } = "";
		public string Date { get; set; } = "";
		public int Tick { get; set; }
	}

	public class OrgEmissionLog {
		public string OrgId { get; set; } = "";
		public List<BotEmission> Emissions { get; set; } = new();
	}

	public class SimulationResult {
		public int Seed { get; set; }
		public SimulationParameters Parameters { get; set; } = new();
		public int TickCount { get; set; }
		public string EndReason { get; set; } = "";
		public string FinalDate { get; set; } = "";
		public List<OrgMetricsResult> Orgs { get; set; } = new();
		public List<TimelineSample> Timeline { get; set; } = new();
		public List<OrgEmissionLog> BotEmissions { get; set; } = new();
	}
}
