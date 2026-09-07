using System.Collections.Generic;

namespace GS.Game.E2E {
	public class RunReport {
		public int ProtocolVersion { get; set; } = E2EProtocol.Version;
		public string RunId { get; set; } = "";
		public string Outcome { get; set; } = "";
		public string Initiator { get; set; } = "agent";
		public bool ReachedMap { get; set; }
		public int StepsCompleted { get; set; }
		public int StepsTotal { get; set; }
		public int? EndingStepIndex { get; set; }
		public string? EndingReason { get; set; }
		public string FailurePolicy { get; set; } = FailurePolicies.StopOnFirstFailure;
		public string ConsoleErrorsPolicy { get; set; } = ConsoleErrorModes.Report;
		public int ConsoleErrorCount { get; set; }
		public List<string> ConsoleErrorQuotes { get; set; } = new List<string>();
		public List<string> SavesCreated { get; set; } = new List<string>();
		public List<string> SavesUsed { get; set; } = new List<string>();
		public string Locale { get; set; } = "";
		public int PanelPathStepCount { get; set; }
		public Dictionary<string, string> ResolvedInputs { get; set; } = new Dictionary<string, string>();
		public RunRequest? Request { get; set; }
		public List<StepReportEntry> Steps { get; set; } = new List<StepReportEntry>();
	}

	public class StepReportEntry {
		public int Index { get; set; }
		public string Kind { get; set; } = "";
		public string? Target { get; set; }
		public string Outcome { get; set; } = "";
		public double DurationSeconds { get; set; }
		public string? ScreenshotPath { get; set; }
		public string? ConsolePath { get; set; }
		public string? StatePath { get; set; }
		public string? FailureReason { get; set; }
		public string? InputPath { get; set; }
	}

	public static class StepOutcomes {
		public const string Succeeded = "succeeded";
		public const string Failed = "failed";
		public const string Skipped = "skipped";
		public const string Timeout = "timeout";
	}
}
