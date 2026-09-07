using System;
using System.IO;
using GS.Game.E2E;
using GS.Unity.E2E;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem;

namespace GS.Editor.E2E {
	public static class E2ERunTeardown {
		public static void FinalizeAndRestore(string reasonIfStillRunning, bool restoreScene = true) {
			if (!File.Exists(E2EPaths.CurrentRunLock)) {
				return;
			}

			CurrentRunLock runLock;
			try {
				runLock = E2EJson.Deserialize<CurrentRunLock>(File.ReadAllText(E2EPaths.CurrentRunLock));
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[E2E] Failed to read current_run.json during teardown: {e.Message}");
				File.Delete(E2EPaths.CurrentRunLock);
				return;
			}

			string reportPath = E2EPaths.ReportJson(runLock.RunId);
			RunReport report = File.Exists(reportPath)
				? RunReportSerializer.Deserialize(File.ReadAllText(reportPath))
				: new RunReport { RunId = runLock.RunId, Outcome = RunOutcomes.Interrupted };

			if (string.IsNullOrEmpty(report.Outcome) || report.Outcome == RunOutcomes.Running) {
				report.Outcome = RunOutcomes.Interrupted;
				report.EndingReason = reasonIfStillRunning;
				File.WriteAllText(reportPath, RunReportSerializer.Serialize(report));
				File.WriteAllText(E2EPaths.ReportMarkdown(runLock.RunId), RunReportRenderer.ToMarkdown(report));
			}

			E2ERunIsolation.DeletePersistent(runLock.RunId);
			RestoreEditorState(runLock.RunId, restoreScene);
			if (restoreScene && File.Exists(E2EPaths.CurrentRunLock)) {
				File.Delete(E2EPaths.CurrentRunLock);
			}
		}

		public static void RestoreEditorState(string runId, bool restoreScene = true) {
			string path = E2EPaths.EditorState(runId);
			if (!File.Exists(path)) {
				return;
			}

			var state = E2EJson.Deserialize<EditorStateSnapshot>(File.ReadAllText(path));
			if (Enum.TryParse(state.EditorInputBehaviorInPlayMode, out InputSettings.EditorInputBehaviorInPlayMode inputBehavior)) {
				InputSystem.settings.editorInputBehaviorInPlayMode = inputBehavior;
			}
			if (Enum.TryParse(state.BackgroundBehavior, out InputSettings.BackgroundBehavior background)) {
				InputSystem.settings.backgroundBehavior = background;
			}
			if (restoreScene && !string.IsNullOrEmpty(state.ActiveScenePath) && File.Exists(state.ActiveScenePath)) {
				EditorSceneManager.OpenScene(state.ActiveScenePath);
			}
		}
	}
}
