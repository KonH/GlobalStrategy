using System;
using UnityEngine;
using VContainer.Unity;
using ECS.Viewer;
using GS.Game.Bots;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.DI {
	public class GameLoopRunner : IStartable, ITickable {
		readonly BotSession _botSession;
		readonly PauseToken _pauseToken;
		readonly SaveFileManager _saveFileManager;

		public GameLoopRunner(BotSession botSession, PauseToken pauseToken, SaveFileManager saveFileManager) {
			_botSession = botSession;
			_pauseToken = pauseToken;
			_saveFileManager = saveFileManager;
		}

		public void Start() {
			string saveName = SceneTransitionArgs.SaveNameToLoad;
			if (saveName != null) {
				_botSession.Logic.LoadState(saveName);
			} else if (SceneTransitionArgs.InitialPlayerCountry == null) {
				var latest = _saveFileManager.GetLastSave();
				if (latest != null) {
					try {
						_botSession.Logic.LoadState(latest.SaveName);
						Debug.Log($"[DevAutoLoad] No transition args set — auto-loading save: {latest.SaveName}");
					} catch (Exception e) {
						Debug.LogWarning($"[DevAutoLoad] Failed to auto-load save '{latest.SaveName}': {e.Message}");
					}
				}
			}
		}

		public void Tick() {
			if (_pauseToken.IsPaused) {
				return;
			}
			_botSession.Update(Time.deltaTime);
		}
	}
}
