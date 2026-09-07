using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GS.Game.E2E;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GS.Unity.E2E {
	public class E2ERunnerHost : MonoBehaviour {
		static E2ESessionBridge _bridge;
		static E2ERunnerHost _instance;

		RunRequest _request;
		RunReport _report;
		E2EConsoleCollector _console;
		StepSequencer _sequencer;
		readonly List<string> _savesSeen = new List<string>();
		bool _finished;
		string _runId;
		float _startedAt;
		int _panelPathCount;

		public static E2ESessionBridge CurrentBridge => _bridge;

		public static void AttachBridge(E2ESessionBridge bridge) {
			_bridge = bridge;
		}

		public static void DetachBridge(E2ESessionBridge bridge) {
			if (_bridge == bridge) {
				_bridge = null;
			}
		}

		void Awake() {
			_instance = this;
		}

		void OnDestroy() {
			if (_instance == this) {
				_instance = null;
			}
			_console?.Stop();
		}

		void Start() {
			StartCoroutine(Run());
		}

		IEnumerator Run() {
			_console = new E2EConsoleCollector();
			_console.Start();

			if (!TryLoadRequest(out var error)) {
				Finish(RunOutcomes.Failed, error);
				yield break;
			}

			_runId = _request.RunId;
			_startedAt = Time.realtimeSinceStartup;
			_report = new RunReport {
				ProtocolVersion = E2EProtocol.Version,
				RunId = _runId,
				Outcome = RunOutcomes.Running,
				Initiator = string.IsNullOrEmpty(_request.Initiator) ? "agent" : _request.Initiator,
				FailurePolicy = string.IsNullOrEmpty(_request.FailurePolicy) ? FailurePolicies.StopOnFirstFailure : _request.FailurePolicy,
				ConsoleErrorsPolicy = string.IsNullOrEmpty(_request.ConsoleErrors) ? ConsoleErrorModes.Report : _request.ConsoleErrors,
				Request = _request,
				ResolvedInputs = new Dictionary<string, string>(_request.Inputs)
			};
			ApplySeededLocale();
			WriteReport();

			Directory.CreateDirectory(E2EPaths.StepsDir(_runId));

			if (string.Equals(_request.Mode, RunModes.Interactive, StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrEmpty(_request.Script)) {
				yield return RunInteractive();
				yield break;
			}

			var script = LoadScript(_request.Script, out error);
			if (script == null) {
				Finish(RunOutcomes.Failed, error);
				yield break;
			}

			FillDefaultOrg();
			yield return PrepareSaveInput(script);
			if (_finished) {
				yield break;
			}

			var substitution = ParameterSubstitution.Apply(script, _report.ResolvedInputs);
			if (!substitution.Success) {
				Finish(RunOutcomes.Failed, substitution.Error);
				yield break;
			}
			script = substitution.Script;

			var validation = StepScriptValidator.Validate(script);
			if (!validation.Success) {
				Finish(RunOutcomes.Failed, validation.Error);
				yield break;
			}

			_report.StepsTotal = _report.Steps.Count + script.Steps.Count;
			_sequencer = new StepSequencer(script, new SequencerOptions {
				FailurePolicy = _report.FailurePolicy,
				ConsoleErrors = _report.ConsoleErrorsPolicy,
				RunCapSeconds = _request.TimeoutSeconds ?? StepSequencer.DefaultRunCapSeconds
			});

			bool priorFailed = false;
			for (int i = 0; i < script.Steps.Count; i++) {
				if (!CheckRunCap(i)) {
					yield break;
				}
				if (!_sequencer.ShouldAttempt(i, priorFailed)) {
					RecordSkipped(_report.Steps.Count, script.Steps[i]);
					continue;
				}
				yield return ExecuteRecordedStep(_report.Steps.Count, script.Steps[i]);
				var last = _report.Steps[_report.Steps.Count - 1];
				if (last.Outcome != StepOutcomes.Succeeded) {
					priorFailed = true;
					if (_sequencer.DecideAfterFailure() == AfterFailureAction.Stop) {
						Finish(RunOutcomes.Failed, last.FailureReason ?? last.Outcome);
						yield break;
					}
				}
			}

			Finish(priorFailed ? RunOutcomes.Failed : RunOutcomes.Succeeded, priorFailed ? _report.EndingReason : null);
		}

		IEnumerator RunInteractive() {
			int idle = _request.IdleTimeoutSeconds ?? 120;
			int cap = _request.TimeoutSeconds ?? StepSequencer.DefaultRunCapSeconds;
			_sequencer = new StepSequencer(new List<StepDefinition>(), new SequencerOptions {
				FailurePolicy = _report.FailurePolicy,
				ConsoleErrors = _report.ConsoleErrorsPolicy,
				RunCapSeconds = cap
			});
			var session = new E2EInteractiveSession(_runId, step => ExecuteRecordedStep(_report.Steps.Count, step), idle);
			yield return session.Run(
				reason => Finish(RunOutcomes.Abandoned, reason),
				() => Finish(_report.Outcome == RunOutcomes.Running ? RunOutcomes.Succeeded : _report.Outcome, _report.EndingReason)
			);
		}

		IEnumerator ExecuteRecordedStep(int index, StepDefinition step) {
			var ctx = new E2EStepContext {
				Step = step,
				Bridge = _bridge,
				TimeoutSeconds = step.TimeoutSeconds ?? StepSequencer.DefaultStepTimeoutSeconds
			};
			int errorsBefore = _console.ErrorCount;
			float started = Time.realtimeSinceStartup;

			if (string.Equals(step.Kind, StepKinds.SleepFrames, StringComparison.Ordinal)) {
				int frames = step.Frames ?? 1;
				for (int i = 0; i < frames; i++) {
					yield return null;
				}
			} else {
				yield return Dispatch(ctx);
			}

			if (!ctx.Failed && _sequencer != null && _sequencer.ShouldFailOnConsoleError(_console.ErrorCount - errorsBefore)) {
				ctx.Fail("Console error in strict mode.");
			}

			bool timedOut = !ctx.Failed && Time.realtimeSinceStartup - started >= ctx.TimeoutSeconds && NeedsTimeout(step);
			if (timedOut) {
				var timeout = _sequencer != null
					? _sequencer.CreateTimeoutFailure(index)
					: new TimeoutFailure { StepIndex = index, Reason = "timeout" };
				ctx.Fail(timeout.Reason);
			}

			yield return CaptureEvidence(index, ctx, started, timedOut);
		}

		static bool NeedsTimeout(StepDefinition step) {
			return step.Kind == StepKinds.WaitFor || step.Kind == StepKinds.SelectOrg;
		}

		IEnumerator Dispatch(E2EStepContext ctx) {
			switch (ctx.Step.Kind) {
				case StepKinds.Click:
					yield return E2EClickStep.Execute(ctx);
					break;
				case StepKinds.SelectOrg:
					yield return E2ESelectOrgStep.Execute(ctx);
					break;
				case StepKinds.SelectRow:
					yield return E2ESelectRowStep.Execute(ctx);
					break;
				case StepKinds.SetValue:
					yield return E2ESetValueStep.Execute(ctx);
					break;
				case StepKinds.Command:
					yield return E2ECommandStep.Execute(ctx);
					break;
				case StepKinds.WaitFor:
					yield return E2EWaitStep.Execute(ctx);
					break;
				case StepKinds.Capture:
					yield return E2ECaptureStep.Execute(ctx);
					break;
				default:
					ctx.Fail($"Unknown step kind '{ctx.Step.Kind}'.");
					break;
			}
		}

		IEnumerator CaptureEvidence(int index, E2EStepContext ctx, float started, bool timedOut) {
			string screenshotRel = Rel(E2EPaths.StepScreenshot(_runId, index));
			string consoleRel = Rel(E2EPaths.StepConsole(_runId, index));
			string stateRel = Rel(E2EPaths.StepState(_runId, index));

			yield return ScreenCaptureUtil.CaptureTo(E2EPaths.StepScreenshot(_runId, index));
			File.WriteAllText(E2EPaths.StepConsole(_runId, index), _console.FlushStep());
			var snapshot = E2EStateSnapshot.Capture(_bridge, ctx.InputPath, ctx.Step.ExtraState);
			File.WriteAllText(E2EPaths.StepState(_runId, index), E2EJson.Serialize(snapshot));

			if (string.Equals(ctx.InputPath, "panel", StringComparison.Ordinal)) {
				_panelPathCount++;
			}

			string outcome = ctx.Failed
				? (timedOut ? StepOutcomes.Timeout : StepOutcomes.Failed)
				: StepOutcomes.Succeeded;
			_report.Steps.Add(new StepReportEntry {
				Index = index,
				Kind = ctx.Step.Kind,
				Target = TargetOf(ctx.Step),
				Outcome = outcome,
				DurationSeconds = Time.realtimeSinceStartup - started,
				ScreenshotPath = screenshotRel,
				ConsolePath = consoleRel,
				StatePath = stateRel,
				FailureReason = ctx.FailureReason,
				InputPath = ctx.InputPath
			});
			if (outcome == StepOutcomes.Succeeded) {
				_report.StepsCompleted++;
			} else {
				_report.EndingStepIndex = index;
				_report.EndingReason = ctx.FailureReason;
			}
			UpdateReachedMap(snapshot);
			TrackSaves();
			_report.PanelPathStepCount = _panelPathCount;
			_report.ConsoleErrorCount = _console.ErrorCount;
			_report.ConsoleErrorQuotes = new List<string>(_console.ErrorQuotes);
			WriteReport();
		}

		void RecordSkipped(int index, StepDefinition step) {
			_report.Steps.Add(new StepReportEntry {
				Index = index,
				Kind = step.Kind,
				Target = TargetOf(step),
				Outcome = StepOutcomes.Skipped
			});
			WriteReport();
		}

		bool CheckRunCap(int stepIndex) {
			int cap = _request.TimeoutSeconds ?? StepSequencer.DefaultRunCapSeconds;
			var check = _sequencer.CheckRunCap(TimeSpan.FromSeconds(Time.realtimeSinceStartup - _startedAt), stepIndex);
			if (!check.Exceeded) {
				return true;
			}
			Finish(RunOutcomes.Failed, check.Reason);
			return false;
		}

		IEnumerator PrepareSaveInput(StepScript script) {
			bool needsSave = false;
			foreach (var step in script.Steps) {
				if (step.Save != null && step.Save.IndexOf("{{save}}", StringComparison.Ordinal) >= 0) {
					needsSave = true;
					break;
				}
			}
			if (!needsSave) {
				yield break;
			}

			yield return WaitForBridge(5f);
			if (_report.ResolvedInputs.TryGetValue("save", out var named) && !string.IsNullOrEmpty(named)) {
				if (_bridge != null && _bridge.Saves != null) {
					bool found = false;
					foreach (var save in _bridge.Saves.ListSaves()) {
						if (save.SaveName == named) {
							found = true;
							break;
						}
					}
					if (!found) {
						Finish(RunOutcomes.Failed, $"Named save '{named}' does not exist and cannot be produced.");
					}
				}
				yield break;
			}

			if (_bridge != null && _bridge.Saves != null) {
				var last = _bridge.Saves.GetLastSave();
				if (last != null) {
					_report.ResolvedInputs["save"] = last.SaveName;
					if (!_report.SavesUsed.Contains(last.SaveName)) {
						_report.SavesUsed.Add(last.SaveName);
					}
					yield break;
				}
			}

			var ctx = new E2EStepContext {
				Bridge = _bridge,
				TimeoutSeconds = StepSequencer.DefaultStepTimeoutSeconds,
				Step = new StepDefinition { Kind = StepKinds.Command, Line = "SaveGame" }
			};
			yield return E2ELoadSaveStep.EnsureSave(ctx, setup => RunNested(setup));
			if (ctx.Failed) {
				Finish(RunOutcomes.Failed, ctx.FailureReason);
				yield break;
			}

			SceneManager.LoadScene("MainMenu");
			float deadline = Time.realtimeSinceStartup + 30f;
			while (Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != "MainMenu") {
				yield return null;
			}
			yield return WaitForBridge(10f);
			var provisioned = _bridge?.Saves?.GetLastSave();
			if (provisioned == null) {
				Finish(RunOutcomes.Failed, "Self-provisioned save is missing after returning to MainMenu.");
				yield break;
			}
			_report.ResolvedInputs["save"] = provisioned.SaveName;
			if (!_report.SavesCreated.Contains(provisioned.SaveName)) {
				_report.SavesCreated.Add(provisioned.SaveName);
			}
			if (!_report.SavesUsed.Contains(provisioned.SaveName)) {
				_report.SavesUsed.Add(provisioned.SaveName);
			}
		}

		IEnumerator RunNested(StepScript script) {
			FillDefaultOrg();
			var substitution = ParameterSubstitution.Apply(script, _report.ResolvedInputs);
			if (!substitution.Success) {
				Finish(RunOutcomes.Failed, substitution.Error);
				yield break;
			}
			foreach (var step in substitution.Script.Steps) {
				yield return ExecuteRecordedStep(_report.Steps.Count, step);
				var last = _report.Steps[_report.Steps.Count - 1];
				if (last.Outcome != StepOutcomes.Succeeded) {
					yield break;
				}
			}
		}

		void FillDefaultOrg() {
			if (_report.ResolvedInputs.ContainsKey("org") && !string.IsNullOrEmpty(_report.ResolvedInputs["org"])) {
				return;
			}
			string org = E2EOrgCatalog.DefaultOrgId();
			_report.ResolvedInputs["org"] = org;
		}

		IEnumerator WaitForBridge(float seconds) {
			float deadline = Time.realtimeSinceStartup + seconds;
			while (_bridge == null && Time.realtimeSinceStartup < deadline) {
				yield return null;
			}
		}

		bool TryLoadRequest(out string error) {
			error = null;
			string lockPath = E2EPaths.CurrentRunLock;
			if (!File.Exists(lockPath)) {
				error = "No current_run.json lock.";
				return false;
			}
			var runLock = E2EJson.Deserialize<CurrentRunLock>(File.ReadAllText(lockPath));
			string requestPath = E2EPaths.RunRequest(runLock.RunId);
			if (!File.Exists(requestPath)) {
				error = $"Run request '{requestPath}' is missing.";
				return false;
			}
			_request = RunRequestSerializer.Deserialize(File.ReadAllText(requestPath));
			return true;
		}

		StepScript LoadScript(string script, out string error) {
			error = null;
			string path = script;
			if (!Path.IsPathRooted(path)) {
				if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
					path += ".json";
				}
				string flow = Path.Combine(E2EPaths.FlowsDir, path);
				string scratch = Path.Combine(E2EPaths.ScratchDir, path);
				if (File.Exists(flow)) {
					path = flow;
				} else if (File.Exists(scratch)) {
					path = scratch;
				} else {
					error = $"Script '{script}' was not found in Docs/E2E/flows or .e2e/scratch.";
					return null;
				}
			}
			if (!File.Exists(path)) {
				error = $"Script '{path}' does not exist.";
				return null;
			}
			return StepScriptSerializer.Deserialize(File.ReadAllText(path));
		}

		void Finish(string outcome, string reason) {
			if (_finished) {
				return;
			}
			_finished = true;
			_report ??= new RunReport { RunId = _runId ?? "" };
			_report.Outcome = outcome;
			if (!string.IsNullOrEmpty(reason)) {
				_report.EndingReason = reason;
			}
			_report.ConsoleErrorCount = _console?.ErrorCount ?? 0;
			_report.ConsoleErrorQuotes = _console != null ? new List<string>(_console.ErrorQuotes) : new List<string>();
			_report.PanelPathStepCount = _panelPathCount;
			if (_report.Steps.Count > _report.StepsTotal) {
				_report.StepsTotal = _report.Steps.Count;
			}
			TrackSaves();
			WriteReport();
		}

		void ApplySeededLocale() {
			string path = E2EPaths.SettingsJson(_runId);
			if (!File.Exists(path)) {
				return;
			}
			var settings = E2EJson.Deserialize<SeededSettingsFile>(File.ReadAllText(path));
			if (!string.IsNullOrEmpty(settings.Locale)) {
				_report.Locale = settings.Locale;
			}
		}

		void WriteReport() {
			if (_report == null || string.IsNullOrEmpty(_runId)) {
				return;
			}
			Directory.CreateDirectory(E2EPaths.RunDir(_runId));
			File.WriteAllText(E2EPaths.ReportJson(_runId), RunReportSerializer.Serialize(_report));
			File.WriteAllText(E2EPaths.ReportMarkdown(_runId), RunReportRenderer.ToMarkdown(_report));
		}

		void UpdateReachedMap(CoreStateSnapshot snapshot) {
			if (string.Equals(snapshot.Scene, "Map", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(snapshot.ActiveOrg)) {
				_report.ReachedMap = true;
			}
		}

		void TrackSaves() {
			if (_bridge?.Saves == null) {
				return;
			}
			foreach (var save in _bridge.Saves.ListSaves()) {
				if (!_savesSeen.Contains(save.SaveName)) {
					_savesSeen.Add(save.SaveName);
					if (!_report.SavesCreated.Contains(save.SaveName)) {
						_report.SavesCreated.Add(save.SaveName);
					}
				}
			}
		}

		static string TargetOf(StepDefinition step) {
			return step.Name ?? step.Label ?? step.Org ?? step.Save ?? step.Line ?? step.Screen ?? step.Control ?? "";
		}

		string Rel(string fullPath) {
			string runDir = E2EPaths.RunDir(_runId);
			if (fullPath.StartsWith(runDir, StringComparison.OrdinalIgnoreCase)) {
				return fullPath.Substring(runDir.Length).TrimStart(Path.DirectorySeparatorChar, '/', '\\').Replace('\\', '/');
			}
			return Path.GetFileName(fullPath);
		}

		sealed class SeededSettingsFile {
			public string Locale { get; set; } = "";
		}
	}
}
