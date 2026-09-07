using System;
using System.Collections.Generic;

namespace GS.Game.E2E {
	public class SequencerOptions {
		public string FailurePolicy { get; set; } = FailurePolicies.StopOnFirstFailure;
		public string ConsoleErrors { get; set; } = ConsoleErrorModes.Report;
		public int DefaultStepTimeoutSeconds { get; set; } = StepSequencer.DefaultStepTimeoutSeconds;
		public int RunCapSeconds { get; set; } = StepSequencer.DefaultRunCapSeconds;
	}

	public class RunCapCheck {
		public bool Exceeded { get; set; }
		public int StepIndex { get; set; }
		public string Reason { get; set; } = "";
	}

	public class TimeoutFailure {
		public string Outcome { get; } = StepOutcomes.Timeout;
		public bool CaptureEvidence { get; } = true;
		public int StepIndex { get; set; }
		public string Reason { get; set; } = "";
	}

	public enum AfterFailureAction {
		Stop,
		Continue
	}

	public sealed class StepSequencer {
		public const int DefaultStepTimeoutSeconds = 30;
		public const int DefaultRunCapSeconds = 300;

		readonly IReadOnlyList<StepDefinition> _steps;
		readonly SequencerOptions _options;

		public StepSequencer(IReadOnlyList<StepDefinition> steps, SequencerOptions options) {
			_steps = steps ?? throw new ArgumentNullException(nameof(steps));
			_options = options ?? throw new ArgumentNullException(nameof(options));
		}

		public StepSequencer(StepScript script, SequencerOptions options)
			: this(script.Steps, options) {
		}

		public string FailurePolicy => _options.FailurePolicy;
		public string ConsoleErrors => _options.ConsoleErrors;

		public int TimeoutSecondsFor(int stepIndex) {
			if (stepIndex < 0 || stepIndex >= _steps.Count) {
				throw new ArgumentOutOfRangeException(nameof(stepIndex));
			}
			return _steps[stepIndex].TimeoutSeconds ?? _options.DefaultStepTimeoutSeconds;
		}

		public AfterFailureAction DecideAfterFailure() {
			return string.Equals(_options.FailurePolicy, FailurePolicies.ContinueOnFailure, StringComparison.Ordinal)
				? AfterFailureAction.Continue
				: AfterFailureAction.Stop;
		}

		public bool ShouldFailOnConsoleError(int consoleErrorCount) {
			return string.Equals(_options.ConsoleErrors, ConsoleErrorModes.Strict, StringComparison.Ordinal)
				&& consoleErrorCount > 0;
		}

		public RunCapCheck CheckRunCap(TimeSpan elapsed, int currentStepIndex) {
			if (elapsed.TotalSeconds < _options.RunCapSeconds) {
				return new RunCapCheck { Exceeded = false, StepIndex = currentStepIndex };
			}

			return new RunCapCheck {
				Exceeded = true,
				StepIndex = currentStepIndex,
				Reason = $"Whole-run cap of {_options.RunCapSeconds}s exceeded while on step {currentStepIndex}."
			};
		}

		public TimeoutFailure CreateTimeoutFailure(int stepIndex) {
			int budget = TimeoutSecondsFor(stepIndex);
			return new TimeoutFailure {
				StepIndex = stepIndex,
				Reason = $"Step {stepIndex} timed out after {budget}s."
			};
		}

		public bool ShouldAttempt(int stepIndex, bool aPriorStepFailed) {
			if (stepIndex < 0 || stepIndex >= _steps.Count) {
				return false;
			}
			if (!aPriorStepFailed) {
				return true;
			}
			return DecideAfterFailure() == AfterFailureAction.Continue;
		}
	}
}
