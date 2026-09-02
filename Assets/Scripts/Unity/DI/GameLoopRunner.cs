using System;
using Unity.Profiling;
using UnityEngine;
using VContainer.Unity;
using ECS.Viewer;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Save;

namespace GS.Unity.DI {
	public class GameLoopRunner : IStartable, ITickable {
		// Separate scopes so the Profiler shows simulation cost apart from the VisualState
		// projection - GameLogic.Update is deliberately not used here for that reason.
		static readonly ProfilerMarker _updateLogicMarker = new ProfilerMarker("GameLoop.UpdateLogic");
		static readonly ProfilerMarker _updateVisualStateMarker = new ProfilerMarker("GameLoop.UpdateVisualState");

		readonly BotSession _botSession;
		readonly PauseToken _pauseToken;
		readonly SaveFileManager _saveFileManager;
		readonly TutorialPresentationTriggers _presentationTriggers;
		readonly SettingsStorage _settings;
		string _lastActiveTutorialId;
		bool _tutorialPrefsApplied;

		public GameLoopRunner(
			BotSession botSession,
			PauseToken pauseToken,
			SaveFileManager saveFileManager,
			TutorialPresentationTriggers presentationTriggers,
			SettingsStorage settings) {
			_botSession = botSession;
			_pauseToken = pauseToken;
			_saveFileManager = saveFileManager;
			_presentationTriggers = presentationTriggers;
			_settings = settings;
		}

		public void Start() {
			ApplyTutorialPreferences();
			string saveName = SceneTransitionArgs.SaveNameToLoad;
			if (saveName != null) {
				_botSession.Logic.LoadState(saveName);
				ReapplyTutorialProgressAfterLoad();
			} else if (SceneTransitionArgs.OrganizationId == null) {
				var latest = _saveFileManager.GetLastSave();
				if (latest != null) {
					try {
						_botSession.Logic.LoadState(latest.SaveName);
						ReapplyTutorialProgressAfterLoad();
						Debug.Log($"[DevAutoLoad] No transition args set — auto-loading save: {latest.SaveName}");
					} catch (Exception e) {
						Debug.LogWarning($"[DevAutoLoad] Failed to auto-load save '{latest.SaveName}': {e}");
					}
				}
			}
		}

		public void Tick() {
			if (_pauseToken.IsPaused) {
				return;
			}
			if (!_tutorialPrefsApplied) {
				ApplyTutorialPreferences();
			}
			_botSession.Logic.SetPresentationTriggers(_presentationTriggers.Values);
			float deltaTime = Time.deltaTime;
			bool logicRan;
			using (_updateLogicMarker.Auto()) {
				logicRan = _botSession.UpdateLogic(deltaTime);
			}
			if (logicRan) {
				using (_updateVisualStateMarker.Auto()) {
					_botSession.UpdateVisualState(deltaTime);
				}
			}
			string activeTutorialId = _botSession.Logic.ActiveTutorialId;
			if (activeTutorialId != _lastActiveTutorialId) {
				_presentationTriggers.ClearTaskEdges();
				_lastActiveTutorialId = activeTutorialId;
			}
		}

		void ApplyTutorialPreferences() {
			GameLogic logic = _botSession.Logic;
			logic.SetTutorialsEnabled(_settings.TutorialsEnabled);
			logic.SetTutorialProgressSink(new SettingsTutorialProgressSink(_settings));
			logic.SeedTutorialProgress(_settings.CompletedTutorialIds);
			logic.Commands.Push(new SetTutorialsEnabledCommand(_settings.TutorialsEnabled));
			_tutorialPrefsApplied = true;
		}

		void ReapplyTutorialProgressAfterLoad() {
			GameLogic logic = _botSession.Logic;
			logic.SetTutorialsEnabled(_settings.TutorialsEnabled);
			logic.SetTutorialProgressSink(new SettingsTutorialProgressSink(_settings));
			logic.SeedTutorialProgress(_settings.CompletedTutorialIds);
			// LoadState clears the command buffer — re-arm enable/disable so force-complete
			// still runs on the next tick when tutorials are already off.
			logic.Commands.Push(new SetTutorialsEnabledCommand(_settings.TutorialsEnabled));
			_tutorialPrefsApplied = true;
		}
	}
}
