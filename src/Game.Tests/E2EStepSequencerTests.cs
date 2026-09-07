using System;
using System.Collections.Generic;
using GS.Game.E2E;
using Xunit;

namespace GS.Game.Tests {
	public class E2EStepSequencerTests {
		static List<StepDefinition> ThreeSteps() {
			return new List<StepDefinition> {
				new StepDefinition { Kind = StepKinds.Click, Name = "a" },
				new StepDefinition { Kind = StepKinds.Click, Name = "b", TimeoutSeconds = 90 },
				new StepDefinition { Kind = StepKinds.Click, Name = "c" }
			};
		}

		[Fact]
		public void stop_on_first_failure_skips_remaining_steps_and_records_the_policy() {
			var sequencer = new StepSequencer(ThreeSteps(), new SequencerOptions {
				FailurePolicy = FailurePolicies.StopOnFirstFailure
			});

			Assert.Equal(FailurePolicies.StopOnFirstFailure, sequencer.FailurePolicy);
			Assert.Equal(AfterFailureAction.Stop, sequencer.DecideAfterFailure());
			Assert.True(sequencer.ShouldAttempt(0, aPriorStepFailed: false));
			Assert.False(sequencer.ShouldAttempt(1, aPriorStepFailed: true));
			Assert.False(sequencer.ShouldAttempt(2, aPriorStepFailed: true));
		}

		[Fact]
		public void continue_on_failure_attempts_remaining_steps() {
			var sequencer = new StepSequencer(ThreeSteps(), new SequencerOptions {
				FailurePolicy = FailurePolicies.ContinueOnFailure
			});

			Assert.Equal(FailurePolicies.ContinueOnFailure, sequencer.FailurePolicy);
			Assert.Equal(AfterFailureAction.Continue, sequencer.DecideAfterFailure());
			Assert.True(sequencer.ShouldAttempt(1, aPriorStepFailed: true));
			Assert.True(sequencer.ShouldAttempt(2, aPriorStepFailed: true));
		}

		[Fact]
		public void per_step_timeout_override_applies_only_to_that_step() {
			var sequencer = new StepSequencer(ThreeSteps(), new SequencerOptions());

			Assert.Equal(StepSequencer.DefaultStepTimeoutSeconds, sequencer.TimeoutSecondsFor(0));
			Assert.Equal(90, sequencer.TimeoutSecondsFor(1));
			Assert.Equal(StepSequencer.DefaultStepTimeoutSeconds, sequencer.TimeoutSecondsFor(2));
		}

		[Fact]
		public void whole_run_cap_fires_even_when_per_step_budgets_remain_and_names_the_step() {
			var sequencer = new StepSequencer(ThreeSteps(), new SequencerOptions {
				RunCapSeconds = 10,
				DefaultStepTimeoutSeconds = 30
			});

			var check = sequencer.CheckRunCap(TimeSpan.FromSeconds(11), currentStepIndex: 1);

			Assert.True(check.Exceeded);
			Assert.Equal(1, check.StepIndex);
			Assert.Contains("step 1", check.Reason);
		}

		[Fact]
		public void console_errors_strict_fails_on_first_error_while_report_continues() {
			var report = new StepSequencer(ThreeSteps(), new SequencerOptions {
				ConsoleErrors = ConsoleErrorModes.Report
			});
			var strict = new StepSequencer(ThreeSteps(), new SequencerOptions {
				ConsoleErrors = ConsoleErrorModes.Strict
			});

			Assert.False(report.ShouldFailOnConsoleError(1));
			Assert.True(strict.ShouldFailOnConsoleError(1));
			Assert.False(strict.ShouldFailOnConsoleError(0));
		}

		[Fact]
		public void step_timeout_is_recorded_as_timeout_failure_with_evidence_capture() {
			var sequencer = new StepSequencer(ThreeSteps(), new SequencerOptions());

			var failure = sequencer.CreateTimeoutFailure(1);

			Assert.Equal(StepOutcomes.Timeout, failure.Outcome);
			Assert.True(failure.CaptureEvidence);
			Assert.Equal(1, failure.StepIndex);
			Assert.Contains("timed out", failure.Reason);
		}
	}
}
