using UnityEngine;
using VContainer.Unity;
using ECS.Viewer;
using GS.Main;
using GS.Unity.Common;

namespace GS.Unity.DI {
	public class GameLoopRunner : IStartable, ITickable {
		readonly GameLogic _logic;
		readonly PauseToken _pauseToken;

		public GameLoopRunner(GameLogic logic, PauseToken pauseToken) {
			_logic = logic;
			_pauseToken = pauseToken;
		}

		public void Start() {
			string? saveName = SceneTransitionArgs.SaveNameToLoad;
			if (saveName != null) {
				_logic.LoadState(saveName);
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
