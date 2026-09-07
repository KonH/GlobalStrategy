using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GS.Game.E2E {
	public readonly struct SubstitutionResult {
		public bool Success { get; }
		public StepScript? Script { get; }
		public string? Error { get; }

		SubstitutionResult(bool success, StepScript? script, string? error) {
			Success = success;
			Script = script;
			Error = error;
		}

		public static SubstitutionResult Ok(StepScript script) => new SubstitutionResult(true, script, null);

		public static SubstitutionResult Fail(string error) => new SubstitutionResult(false, null, error);
	}

	public static class ParameterSubstitution {
		static readonly Regex Placeholder = new Regex(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

		public static SubstitutionResult Apply(StepScript script, IReadOnlyDictionary<string, string> inputs) {
			var clone = StepScriptSerializer.Deserialize(StepScriptSerializer.Serialize(script));
			for (int i = 0; i < clone.Steps.Count; i++) {
				var step = clone.Steps[i];
				if (!TrySubstitute(step.Name, inputs, clone.Name, i, out var name, out var error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Name = name;
				if (!TrySubstitute(step.Label, inputs, clone.Name, i, out var label, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Label = label;
				if (!TrySubstitute(step.Org, inputs, clone.Name, i, out var org, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Org = org;
				if (!TrySubstitute(step.Save, inputs, clone.Name, i, out var save, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Save = save;
				if (!TrySubstitute(step.Value, inputs, clone.Name, i, out var value, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Value = value;
				if (!TrySubstitute(step.Line, inputs, clone.Name, i, out var line, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Line = line;
				if (!TrySubstitute(step.Screen, inputs, clone.Name, i, out var screen, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Screen = screen;
				if (!TrySubstitute(step.Control, inputs, clone.Name, i, out var control, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.Control = control;
				if (!TrySubstitute(step.GameDate, inputs, clone.Name, i, out var gameDate, out error)) {
					return SubstitutionResult.Fail(error!);
				}
				step.GameDate = gameDate;
			}
			return SubstitutionResult.Ok(clone);
		}

		static bool TrySubstitute(
			string? value,
			IReadOnlyDictionary<string, string> inputs,
			string scriptName,
			int stepIndex,
			out string? replaced,
			out string? error
		) {
			error = null;
			replaced = value;
			if (string.IsNullOrEmpty(value)) {
				return true;
			}

			var matches = Placeholder.Matches(value);
			if (matches.Count == 0) {
				return true;
			}

			string current = value;
			for (int i = 0; i < matches.Count; i++) {
				var match = matches[i];
				string key = match.Groups[1].Value;
				if (!inputs.TryGetValue(key, out var replacement)) {
					error = $"{scriptName} step {stepIndex}: unresolved placeholder '{{{{{key}}}}}'.";
					return false;
				}
				current = current.Replace(match.Value, replacement);
			}

			replaced = current;
			return true;
		}
	}
}
