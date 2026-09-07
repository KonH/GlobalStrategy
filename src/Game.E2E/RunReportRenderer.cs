using System.Text;

namespace GS.Game.E2E {
	public static class RunReportRenderer {
		public static string ToMarkdown(RunReport report) {
			var sb = new StringBuilder();
			sb.AppendLine("# E2E Run Report");
			sb.AppendLine();
			sb.AppendLine("## Summary");
			sb.AppendLine();
			sb.AppendLine($"- **Outcome:** {report.Outcome}");
			sb.AppendLine($"- **Reached map:** {report.ReachedMap}");
			sb.AppendLine($"- **Steps:** {report.StepsCompleted}/{report.StepsTotal}");
			if (report.EndingStepIndex != null) {
				sb.AppendLine($"- **Ending step:** {report.EndingStepIndex}");
			}
			if (!string.IsNullOrEmpty(report.EndingReason)) {
				sb.AppendLine($"- **Ending reason:** {report.EndingReason}");
			}
			sb.AppendLine($"- **Failure policy:** {report.FailurePolicy}");
			sb.AppendLine($"- **Console errors policy:** {report.ConsoleErrorsPolicy}");
			sb.AppendLine($"- **Console errors:** {report.ConsoleErrorCount}");
			if (report.ConsoleErrorQuotes.Count > 0) {
				sb.AppendLine("- **Console error quotes:**");
				foreach (var quote in report.ConsoleErrorQuotes) {
					sb.AppendLine($"  - {quote}");
				}
			}
			if (report.SavesCreated.Count > 0) {
				sb.AppendLine($"- **Saves created:** {string.Join(", ", report.SavesCreated)}");
			}
			if (report.SavesUsed.Count > 0) {
				sb.AppendLine($"- **Saves used:** {string.Join(", ", report.SavesUsed)}");
			}
			if (!string.IsNullOrEmpty(report.Locale)) {
				sb.AppendLine($"- **Locale:** {report.Locale}");
			}
			if (report.PanelPathStepCount > 0) {
				sb.AppendLine($"- **Panel-path steps:** {report.PanelPathStepCount}");
			}
			if (report.ResolvedInputs.Count > 0) {
				sb.AppendLine("- **Resolved inputs:**");
				foreach (var pair in report.ResolvedInputs) {
					sb.AppendLine($"  - {pair.Key}: {pair.Value}");
				}
			}
			if (string.Equals(report.Outcome, RunOutcomes.Interrupted, System.StringComparison.Ordinal)
				|| string.Equals(report.Outcome, RunOutcomes.Abandoned, System.StringComparison.Ordinal)
				|| string.Equals(report.Outcome, RunOutcomes.Crashed, System.StringComparison.Ordinal)) {
				sb.AppendLine();
				sb.AppendLine("This run is **incomplete**.");
			}

			sb.AppendLine();
			sb.AppendLine("## Steps");
			sb.AppendLine();
			foreach (var step in report.Steps) {
				sb.AppendLine($"### Step {step.Index}: {step.Kind}");
				sb.AppendLine();
				if (!string.IsNullOrEmpty(step.Target)) {
					sb.AppendLine($"- **Target:** {step.Target}");
				}
				sb.AppendLine($"- **Outcome:** {step.Outcome}");
				sb.AppendLine($"- **Duration:** {step.DurationSeconds:0.###}s");
				if (!string.IsNullOrEmpty(step.FailureReason)) {
					sb.AppendLine($"- **Failure reason:** {step.FailureReason}");
				}
				if (!string.IsNullOrEmpty(step.ScreenshotPath)) {
					sb.AppendLine($"- **Screenshot:** {step.ScreenshotPath}");
				}
				if (!string.IsNullOrEmpty(step.ConsolePath)) {
					sb.AppendLine($"- **Console:** {step.ConsolePath}");
				}
				if (!string.IsNullOrEmpty(step.StatePath)) {
					sb.AppendLine($"- **State:** {step.StatePath}");
				}
				sb.AppendLine();
			}

			return sb.ToString();
		}
	}
}
