using GS.Main;
using VContainer.Unity;

namespace GS.Unity.DI {
	public class StaticGameLoopRunner : ITickable {
		readonly StaticGameLogic _logic;

		public StaticGameLoopRunner(StaticGameLogic logic) {
			_logic = logic;
		}

		public void Tick() {
			_logic.Update();
		}
	}
}
