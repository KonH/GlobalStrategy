using System;
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
			_botSession.Update(Time.deltaTime);
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
