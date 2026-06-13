using System;
using UnityEngine;
using VContainer.Unity;
using ECS.Viewer;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.DI {
	public class GameLoopRunner : IStartable, ITickable {
		readonly GameLogic _logic;
		readonly PauseToken _pauseToken;
		readonly SaveFileManager _saveFileManager;

		public GameLoopRunner(GameLogic logic, PauseToken pauseToken, SaveFileManager saveFileManager) {
			_logic = logic;
			_pauseToken = pauseToken;
			_saveFileManager = saveFileManager;
		}

		public void Start() {
			string saveName = SceneTransitionArgs.SaveNameToLoad;
			if (saveName != null) {
				_logic.LoadState(saveName);
			} else if (SceneTransitionArgs.InitialPlayerCountry == null) {
				var latest = _saveFileManager.GetLastSave();
				if (latest != null) {
					try {
						_logic.LoadState(latest.SaveName);
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
			_logic.Update(Time.deltaTime);
		}
	}
}
