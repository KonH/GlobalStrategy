using System;
using System.Collections.Generic;

namespace GS.Game.E2E {
	public readonly struct ValidationResult {
		public bool Success { get; }
		public string? Error { get; }

		ValidationResult(bool success, string? error) {
			Success = success;
			Error = error;
		}

		public static ValidationResult Ok() => new ValidationResult(true, null);

		public static ValidationResult Fail(string scriptName, int stepIndex, string message) {
			return new ValidationResult(false, $"{scriptName} step {stepIndex}: {message}");
		}
	}

	public static class StepScriptValidator {
		static readonly HashSet<string> KnownKinds = new HashSet<string>(StringComparer.Ordinal) {
			StepKinds.Click,
			StepKinds.SelectOrg,
			StepKinds.SelectRow,
			StepKinds.SetValue,
			StepKinds.Command,
			StepKinds.WaitFor,
			StepKinds.Capture,
			StepKinds.SleepFrames
		};

		public static ValidationResult Validate(StepScript script) {
			string scriptName = string.IsNullOrEmpty(script.Name) ? "<unnamed>" : script.Name;
			if (script.Steps == null) {
				return ValidationResult.Fail(scriptName, 0, "script has no steps list.");
			}

			for (int i = 0; i < script.Steps.Count; i++) {
				var step = script.Steps[i];
				if (step == null || string.IsNullOrWhiteSpace(step.Kind)) {
					return ValidationResult.Fail(scriptName, i, "step is missing a kind.");
				}
				if (!KnownKinds.Contains(step.Kind)) {
					return ValidationResult.Fail(scriptName, i, $"unknown step kind '{step.Kind}'.");
				}

				string? missing = MissingTarget(step);
				if (missing != null) {
					return ValidationResult.Fail(scriptName, i, missing);
				}
			}

			return ValidationResult.Ok();
		}

		static string? MissingTarget(StepDefinition step) {
			switch (step.Kind) {
				case StepKinds.Click:
					if (string.IsNullOrWhiteSpace(step.Name) && string.IsNullOrWhiteSpace(step.Label)) {
						return "click step is missing a name or label target.";
					}
					return null;
				case StepKinds.SelectOrg:
					if (string.IsNullOrWhiteSpace(step.Org)) {
						return "selectOrg step is missing an org target.";
					}
					return null;
				case StepKinds.SelectRow:
					if (string.IsNullOrWhiteSpace(step.Name)) {
						return "selectRow step is missing a list name target.";
					}
					if (step.Row == null && string.IsNullOrWhiteSpace(step.Save)) {
						return "selectRow step is missing a row index or save target.";
					}
					return null;
				case StepKinds.SetValue:
					if (string.IsNullOrWhiteSpace(step.Name) && string.IsNullOrWhiteSpace(step.Label)) {
						return "setValue step is missing a name or label target.";
					}
					if (step.Value == null) {
						return "setValue step is missing a value.";
					}
					return null;
				case StepKinds.Command:
					if (string.IsNullOrWhiteSpace(step.Line)) {
						return "command step is missing a line.";
					}
					return null;
				case StepKinds.WaitFor:
					if (string.IsNullOrWhiteSpace(step.Screen)
						&& string.IsNullOrWhiteSpace(step.Control)
						&& string.IsNullOrWhiteSpace(step.GameDate)
						&& step.Pause == null) {
						return "waitFor step is missing a screen, control, gameDate, or pause condition.";
					}
					return null;
				case StepKinds.SleepFrames:
					if (step.Frames == null || step.Frames.Value <= 0) {
						return "sleepFrames step is missing a positive frames count.";
					}
					return null;
				default:
					return null;
			}
		}
	}
}
