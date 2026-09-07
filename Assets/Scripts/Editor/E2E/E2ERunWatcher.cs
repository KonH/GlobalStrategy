using System;
using System.Globalization;
using System.IO;
using GS.Game.E2E;
using GS.Unity.E2E;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GS.Editor.E2E {
	[InitializeOnLoad]
	public static class E2ERunWatcher {
		const float PollInterval = 0.25f;
		const int KeepRunFolders = 20;
		const int WatchdogGraceSeconds = 30;
		const int EnterPlayGraceSeconds = 30;
		const string EnteringPlayPref = "GS.E2E.EnteringPlay";

		static double _nextPoll;

		static E2ERunWatcher() {
			EditorApplication.update += OnUpdate;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
		}

		static void RecoverStaleLock() {
			if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) {
				return;
			}
			if (!File.Exists(E2EPaths.CurrentRunLock)) {
				return;
			}

			CurrentRunLock runLock;
			try {
				runLock = E2EJson.Deserialize<CurrentRunLock>(File.ReadAllText(E2EPaths.CurrentRunLock));
			} catch (Exception e) {
				Debug.LogError($"[E2E] Stale lock unreadable: {e.Message}");
				File.Delete(E2EPaths.CurrentRunLock);
				return;
			}

			if (TryParseTime(runLock.StartedAt, out var started)
				&& (DateTime.UtcNow - started).TotalSeconds < EnterPlayGraceSeconds) {
				return;
			}

			string reportPath = E2EPaths.ReportJson(runLock.RunId);
			var report = File.Exists(reportPath)
				? RunReportSerializer.Deserialize(File.ReadAllText(reportPath))
				: new RunReport { RunId = runLock.RunId };
			if (string.IsNullOrEmpty(report.Outcome) || report.Outcome == RunOutcomes.Running) {
				report.Outcome = RunOutcomes.Crashed;
				report.EndingReason = "Editor was not playing when the run lock was found; treating as crashed.";
				Directory.CreateDirectory(E2EPaths.RunDir(runLock.RunId));
				File.WriteAllText(reportPath, RunReportSerializer.Serialize(report));
				File.WriteAllText(E2EPaths.ReportMarkdown(runLock.RunId), RunReportRenderer.ToMarkdown(report));
			}

			SetEnteringPlay(false);
			E2ERunIsolation.DeletePersistent(runLock.RunId);
			E2ERunTeardown.RestoreEditorState(runLock.RunId);
			File.Delete(E2EPaths.CurrentRunLock);
		}

		static void OnUpdate() {
			if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
				return;
			}

			if (EditorApplication.timeSinceStartup < _nextPoll) {
				CheckWatchdogAndFinished();
				return;
			}
			_nextPoll = EditorApplication.timeSinceStartup + PollInterval;
			CheckWatchdogAndFinished();
			RecoverStaleLock();
			PollRequests();
		}

		static void CheckWatchdogAndFinished() {
			if (!File.Exists(E2EPaths.CurrentRunLock)) {
				return;
			}

			CurrentRunLock runLock;
			try {
				runLock = E2EJson.Deserialize<CurrentRunLock>(File.ReadAllText(E2EPaths.CurrentRunLock));
			} catch {
				return;
			}

			if (EditorApplication.isPlaying) {
				string reportPath = E2EPaths.ReportJson(runLock.RunId);
				if (File.Exists(reportPath)) {
					var report = RunReportSerializer.Deserialize(File.ReadAllText(reportPath));
					if (!string.IsNullOrEmpty(report.Outcome) && report.Outcome != RunOutcomes.Running) {
						if (report.Request != null && report.Request.LeavePlayRunning) {
							return;
						}
						EditorApplication.isPlaying = false;
						return;
					}
					int cap = report.Request?.TimeoutSeconds ?? StepSequencer.DefaultRunCapSeconds;
					if (TryParseTime(runLock.StartedAt, out var started)
						&& (DateTime.UtcNow - started).TotalSeconds > cap + WatchdogGraceSeconds) {
						EditorApplication.isPlaying = false;
					}
				}
			}
		}

		static void PollRequests() {
			Directory.CreateDirectory(E2EPaths.RequestsDir);
			string[] files;
			try {
				files = Directory.GetFiles(E2EPaths.RequestsDir, "*.json");
			} catch {
				return;
			}
			if (files.Length == 0) {
				return;
			}

			Array.Sort(files);
			AcceptOrRefuse(files[0]);
		}

		static void AcceptOrRefuse(string requestPath) {
			string json = File.ReadAllText(requestPath);
			File.Delete(requestPath);

			RunRequest request;
			try {
				request = RunRequestSerializer.Deserialize(json);
			} catch (Exception e) {
				Debug.LogError($"[E2E] Invalid run request: {e.Message}");
				return;
			}

			if (string.IsNullOrEmpty(request.RunId)) {
				request.RunId = Path.GetFileNameWithoutExtension(requestPath);
			}

			string refuse = RefusalReason(request);
			if (refuse != null) {
				WriteRefusal(request, refuse);
				return;
			}

			Accept(request);
		}

		static string RefusalReason(RunRequest request) {
			if (EditorApplication.isPlaying) {
				return "A play session is already active.";
			}
			if (File.Exists(E2EPaths.CurrentRunLock)) {
				return "A run lock already exists.";
			}
			if (AnyDirtyScene()) {
				return "An open scene is dirty; refusing so restoration cannot destroy unsaved work.";
			}
			if (!ProtocolChecker.IsMatch(request.ProtocolVersion)) {
				return null;
			}
			return null;
		}

		static void Accept(RunRequest request) {
			if (!ProtocolChecker.IsMatch(request.ProtocolVersion)) {
				Directory.CreateDirectory(E2EPaths.RunDir(request.RunId));
				var mismatch = ProtocolChecker.CreateMismatchReport(request.ProtocolVersion);
				mismatch.RunId = request.RunId;
				mismatch.Request = request;
				File.WriteAllText(E2EPaths.ReportJson(request.RunId), RunReportSerializer.Serialize(mismatch));
				File.WriteAllText(E2EPaths.ReportMarkdown(request.RunId), RunReportRenderer.ToMarkdown(mismatch));
				return;
			}

			PruneRuns();
			Directory.CreateDirectory(E2EPaths.RunDir(request.RunId));
			Directory.CreateDirectory(E2EPaths.StepsDir(request.RunId));
			File.WriteAllText(E2EPaths.RunRequest(request.RunId), RunRequestSerializer.Serialize(request));

			var editorState = new EditorStateSnapshot {
				ActiveScenePath = SceneManager.GetActiveScene().path,
				EditorInputBehaviorInPlayMode = InputSystem.settings.editorInputBehaviorInPlayMode.ToString(),
				BackgroundBehavior = InputSystem.settings.backgroundBehavior.ToString()
			};
			File.WriteAllText(E2EPaths.EditorState(request.RunId), E2EJson.Serialize(editorState));

			E2ERunIsolation.Prepare(request.RunId, request);

			InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
			InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

			EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");

			var runLock = new CurrentRunLock {
				RunId = request.RunId,
				StartedAt = DateTime.UtcNow.ToString("o"),
				RunFolder = E2EPaths.RunDir(request.RunId)
			};
			File.WriteAllText(E2EPaths.CurrentRunLock, E2EJson.Serialize(runLock));
			SetEnteringPlay(true);
			EditorApplication.isPlaying = true;
		}

		static void WriteRefusal(RunRequest request, string reason) {
			Directory.CreateDirectory(E2EPaths.RunDir(request.RunId));
			var report = new RunReport {
				RunId = request.RunId,
				Outcome = RunOutcomes.Refused,
				EndingReason = reason,
				Initiator = request.Initiator,
				Request = request
			};
			File.WriteAllText(E2EPaths.ReportJson(request.RunId), RunReportSerializer.Serialize(report));
			File.WriteAllText(E2EPaths.ReportMarkdown(request.RunId), RunReportRenderer.ToMarkdown(report));
		}

		static void SetEnteringPlay(bool value) {
			SessionState.SetBool("E2E.EnteringPlay", value);
			EditorPrefs.SetBool(EnteringPlayPref, value);
		}

		static bool IsEnteringPlay() {
			return SessionState.GetBool("E2E.EnteringPlay", false)
				|| EditorPrefs.GetBool(EnteringPlayPref, false);
		}

		static void OnPlayModeStateChanged(PlayModeStateChange change) {
			if (change == PlayModeStateChange.EnteredPlayMode) {
				SetEnteringPlay(false);
			}
			if (change == PlayModeStateChange.ExitingPlayMode) {
				SetEnteringPlay(false);
				if (File.Exists(E2EPaths.CurrentRunLock)) {
					E2ERunTeardown.FinalizeAndRestore("interrupted", restoreScene: false);
				}
			}
			if (change == PlayModeStateChange.EnteredEditMode) {
				SetEnteringPlay(false);
				if (File.Exists(E2EPaths.CurrentRunLock)) {
					E2ERunTeardown.FinalizeAndRestore("interrupted", restoreScene: true);
				}
			}
		}

		static void OnBeforeAssemblyReload() {
			if (IsEnteringPlay()
				|| (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)) {
				return;
			}
			if (File.Exists(E2EPaths.CurrentRunLock) && EditorApplication.isPlaying) {
				E2ERunTeardown.FinalizeAndRestore("interrupted (assembly reload)", restoreScene: false);
				EditorApplication.isPlaying = false;
			}
		}

		static bool AnyDirtyScene() {
			for (int i = 0; i < SceneManager.sceneCount; i++) {
				if (SceneManager.GetSceneAt(i).isDirty) {
					return true;
				}
			}
			return false;
		}

		static void PruneRuns() {
			string runsRoot = Path.Combine(E2EPaths.Root, "runs");
			if (!Directory.Exists(runsRoot)) {
				return;
			}
			var dirs = new DirectoryInfo(runsRoot).GetDirectories();
			Array.Sort(dirs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
			for (int i = KeepRunFolders; i < dirs.Length; i++) {
				try {
					dirs[i].Delete(true);
				} catch (Exception e) {
					Debug.LogWarning($"[E2E] Failed to prune '{dirs[i].Name}': {e.Message}");
				}
			}
		}

		static bool TryParseTime(string value, out DateTime time) {
			return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
		}
	}
}
