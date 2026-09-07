using GS.Game.E2E;

namespace GS.Unity.E2E {
	public class E2EStepContext {
		public StepDefinition Step;
		public E2ESessionBridge Bridge;
		public float TimeoutSeconds;
		public string InputPath = "device";
		public bool Failed;
		public string FailureReason;

		public void Fail(string reason) {
			Failed = true;
			FailureReason = reason;
		}

		public void SetInputPath(string path) {
			InputPath = path;
		}
	}
}
