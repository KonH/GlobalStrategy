using UnityEngine;
using VContainer.Unity;
using ECS.Viewer;
using GS.Main;

namespace GS.Unity.DI {
	public class GameLoopRunner : ITickable {
		readonly GameLogic _logic;
		readonly PauseToken _pauseToken;

		public GameLoopRunner(GameLogic logic, PauseToken pauseToken) {
			_logic = logic;
			_pauseToken = pauseToken;
		}

		public void Tick() {
			if (_pauseToken.IsPaused) {
				return;
			}
			_logic.Update(Time.deltaTime);
		}
	}
}
