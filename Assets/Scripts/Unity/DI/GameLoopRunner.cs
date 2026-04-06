using UnityEngine;
using VContainer.Unity;
using GS.Main;

namespace GS.Unity.DI {
	public class GameLoopRunner : ITickable {
		readonly GameLogic _logic;

		public GameLoopRunner(GameLogic logic) {
			_logic = logic;
		}

		public void Tick() {
			_logic.Update(Time.deltaTime);
		}
	}
}
