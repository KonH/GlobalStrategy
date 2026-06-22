using UnityEngine;
using VContainer.Unity;
using GS.Main;

namespace GS.Unity.UI {
	class AnimationBarrierDriver : ITickable {
		readonly VisualState _state;

		public AnimationBarrierDriver(VisualState state) {
			_state = state;
		}

		public void Tick() {
			float dt = Time.deltaTime;
			_state.PlayerGold.Tick(dt);
			_state.SelectedCountryUsedInfluence.Tick(dt);
			foreach (var animatable in _state.CharacterOpinions.Values) {
				animatable.Tick(dt);
			}
		}
	}
}
