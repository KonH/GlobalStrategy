using System.Collections.Generic;

namespace GS.Game.E2E {
	public class RunRequest {
		public int ProtocolVersion { get; set; } = E2EProtocol.Version;
		public string RunId { get; set; } = "";
		public string? Script { get; set; }
		public string Initiator { get; set; } = "agent";
		public string Mode { get; set; } = RunModes.Scripted;
		public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();
		public string FailurePolicy { get; set; } = FailurePolicies.StopOnFirstFailure;
		public string ConsoleErrors { get; set; } = ConsoleErrorModes.Report;
		public int? TimeoutSeconds { get; set; }
		public int? IdleTimeoutSeconds { get; set; }
		public RunRequestSettings? Settings { get; set; }
	}

	public class RunRequestSettings {
		public bool? TutorialsEnabled { get; set; }
	}

	public class CurrentRunLock {
		public string RunId { get; set; } = "";
		public string StartedAt { get; set; } = "";
		public string RunFolder { get; set; } = "";
	}

	public class EditorStateSnapshot {
		public string ActiveScenePath { get; set; } = "";
		public string EditorInputBehaviorInPlayMode { get; set; } = "";
		public string BackgroundBehavior { get; set; } = "";
	}
}
