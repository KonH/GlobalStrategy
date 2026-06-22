using UnityEngine;
using VContainer.Unity;
using GS.Main;

namespace GS.Unity.UI {
	public class AnimationBarrierDriver : ITickable {
		readonly VisualState _state;

		public AnimationBarrierDriver(VisualState state) {
			_state = state;
		}

		public void Tick() {
			float dt = Time.deltaTime;
			_state.PlayerGold.Tick(dt);
			_state.SelectedCountryUsedInfluence.Tick(dt);
			var opinions = new System.Collections.Generic.List<AnimatableInt>(_state.CharacterOpinions.Values);
			foreach (var animatable in opinions) {
				animatable.Tick(dt);
			}
		}
	}
}
