namespace GS.Game.E2E {
	public static class E2EProtocol {
		public const int Version = 1;
	}

	public static class RunOutcomes {
		public const string Succeeded = "succeeded";
		public const string Failed = "failed";
		public const string Interrupted = "interrupted";
		public const string Abandoned = "abandoned";
		public const string Crashed = "crashed";
		public const string ProtocolMismatch = "protocol-mismatch";
		public const string Refused = "refused";
		public const string Running = "running";
	}

	public static class FailurePolicies {
		public const string StopOnFirstFailure = "stopOnFirstFailure";
		public const string ContinueOnFailure = "continueOnFailure";
	}

	public static class ConsoleErrorModes {
		public const string Report = "report";
		public const string Strict = "strict";
	}

	public static class StepKinds {
		public const string Click = "click";
		public const string SelectOrg = "selectOrg";
		public const string SelectRow = "selectRow";
		public const string SetValue = "setValue";
		public const string Command = "command";
		public const string WaitFor = "waitFor";
		public const string Capture = "capture";
		public const string SleepFrames = "sleepFrames";
	}

	public static class RunModes {
		public const string Scripted = "scripted";
		public const string Interactive = "interactive";
	}

	public static class ProtocolChecker {
		public static bool IsMatch(int requestVersion) {
			return requestVersion == E2EProtocol.Version;
		}

		public static RunReport CreateMismatchReport(int requestVersion) {
			return new RunReport {
				Outcome = RunOutcomes.ProtocolMismatch,
				EndingReason = $"Request protocolVersion {requestVersion} does not match runner protocolVersion {E2EProtocol.Version}."
			};
		}
	}
}
