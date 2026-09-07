using System.IO;
using UnityEngine;

namespace GS.Unity.E2E {
	public static class E2EPaths {
		public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
		public static string Root => Path.Combine(ProjectRoot, ".e2e");
		public static string RequestsDir => Path.Combine(Root, "requests");
		public static string CurrentRunLock => Path.Combine(Root, "current_run.json");
		public static string FlowsDir => Path.Combine(ProjectRoot, "Docs", "E2E", "flows");
		public static string ScratchDir => Path.Combine(Root, "scratch");
		public static string OrganizationsConfig => Path.Combine(Application.dataPath, "Configs", "organizations.json");

		public static string RunDir(string runId) => Path.Combine(Root, "runs", runId);
		public static string RunRequest(string runId) => Path.Combine(RunDir(runId), "request.json");
		public static string EditorState(string runId) => Path.Combine(RunDir(runId), "editor_state.json");
		public static string ReportJson(string runId) => Path.Combine(RunDir(runId), "report.json");
		public static string ReportMarkdown(string runId) => Path.Combine(RunDir(runId), "report.md");
		public static string PersistentDir(string runId) => Path.Combine(RunDir(runId), "persistent");
		public static string StepsDir(string runId) => Path.Combine(RunDir(runId), "steps");
		public static string InboxDir(string runId) => Path.Combine(RunDir(runId), "inbox");
		public static string OutboxDir(string runId) => Path.Combine(RunDir(runId), "outbox");
		public static string SettingsJson(string runId) => Path.Combine(PersistentDir(runId), "settings.json");

		public static string StepScreenshot(string runId, int index) =>
			Path.Combine(StepsDir(runId), $"{index:000}_screenshot.png");
		public static string StepConsole(string runId, int index) =>
			Path.Combine(StepsDir(runId), $"{index:000}_console.log");
		public static string StepState(string runId, int index) =>
			Path.Combine(StepsDir(runId), $"{index:000}_state.json");
	}
}
