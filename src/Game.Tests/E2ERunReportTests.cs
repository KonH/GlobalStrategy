using System.Collections.Generic;
using GS.Game.E2E;
using Xunit;

namespace GS.Game.Tests {
	public class E2ERunReportTests {
		static RunReport FailedMidSequence() {
			return new RunReport {
				RunId = "run-1",
				Outcome = RunOutcomes.Failed,
				ReachedMap = false,
				StepsCompleted = 1,
				StepsTotal = 3,
				EndingStepIndex = 1,
				EndingReason = "control 'btn-start' was not interactive",
				FailurePolicy = FailurePolicies.StopOnFirstFailure,
				ConsoleErrorsPolicy = ConsoleErrorModes.Report,
				ConsoleErrorCount = 2,
				ConsoleErrorQuotes = new List<string> {
					"NullReferenceException: boom",
					"MissingReferenceException: gone"
				},
				Steps = new List<StepReportEntry> {
					new StepReportEntry {
						Index = 0,
						Kind = StepKinds.Click,
						Target = "btn-play",
						Outcome = StepOutcomes.Succeeded,
						DurationSeconds = 0.4,
						ScreenshotPath = "steps/000_screenshot.png",
						ConsolePath = "steps/000_console.log",
						StatePath = "steps/000_state.json"
					},
					new StepReportEntry {
						Index = 1,
						Kind = StepKinds.Click,
						Target = "btn-start",
						Outcome = StepOutcomes.Failed,
						DurationSeconds = 1.2,
						ScreenshotPath = "steps/001_screenshot.png",
						ConsolePath = "steps/001_console.log",
						StatePath = "steps/001_state.json",
						FailureReason = "control 'btn-start' was not interactive"
					}
				}
			};
		}

		[Fact]
		public void mid_sequence_failure_summary_carries_outcome_reached_map_counts_and_policy() {
			string markdown = RunReportRenderer.ToMarkdown(FailedMidSequence());

			Assert.Contains("**Outcome:** failed", markdown);
			Assert.Contains("**Reached map:** False", markdown);
			Assert.Contains("**Steps:** 1/3", markdown);
			Assert.Contains("**Ending step:** 1", markdown);
			Assert.Contains("control 'btn-start' was not interactive", markdown);
			Assert.Contains("**Failure policy:** stopOnFirstFailure", markdown);
		}

		[Fact]
		public void console_errors_appear_in_the_summary() {
			string markdown = RunReportRenderer.ToMarkdown(FailedMidSequence());

			Assert.Contains("**Console errors:** 2", markdown);
			Assert.Contains("NullReferenceException: boom", markdown);
			Assert.Contains("MissingReferenceException: gone", markdown);
		}

		[Fact]
		public void every_step_entry_carries_relative_evidence_paths() {
			string markdown = RunReportRenderer.ToMarkdown(FailedMidSequence());

			Assert.Contains("steps/000_screenshot.png", markdown);
			Assert.Contains("steps/000_console.log", markdown);
			Assert.Contains("steps/000_state.json", markdown);
			Assert.Contains("steps/001_screenshot.png", markdown);
			Assert.Contains("steps/001_console.log", markdown);
			Assert.Contains("steps/001_state.json", markdown);
		}

		[Fact]
		public void interrupted_run_renders_as_incomplete_with_completed_steps_intact() {
			var report = FailedMidSequence();
			report.Outcome = RunOutcomes.Interrupted;
			report.EndingReason = "interrupted (assembly reload)";

			string markdown = RunReportRenderer.ToMarkdown(report);

			Assert.Contains("incomplete", markdown);
			Assert.Contains("interrupted (assembly reload)", markdown);
			Assert.Contains("Step 0: click", markdown);
			Assert.Contains("steps/000_screenshot.png", markdown);
		}

		[Fact]
		public void serializer_round_trips_per_step_evidence_paths() {
			var original = FailedMidSequence();

			string json = RunReportSerializer.Serialize(original);
			var restored = RunReportSerializer.Deserialize(json);

			Assert.Equal(original.Outcome, restored.Outcome);
			Assert.Equal(original.Steps.Count, restored.Steps.Count);
			Assert.Equal(original.Steps[0].ScreenshotPath, restored.Steps[0].ScreenshotPath);
			Assert.Equal(original.Steps[0].ConsolePath, restored.Steps[0].ConsolePath);
			Assert.Equal(original.Steps[0].StatePath, restored.Steps[0].StatePath);
			Assert.Equal(original.Steps[1].ScreenshotPath, restored.Steps[1].ScreenshotPath);
			Assert.Contains("\"screenshotPath\":", json);
		}
	}
}
