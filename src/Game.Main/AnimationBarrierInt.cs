using System;

namespace GS.Main {
	public class AnimationBarrierInt {
		int _initialOffset;
		float _duration;
		float _accumulated;

		public int Offset { get; private set; }
		public bool IsComplete => Offset == 0;

		internal AnimationBarrierInt(int offset, float duration) {
			_initialOffset = offset;
			Offset = offset;
			_duration = duration;
			_accumulated = 0f;
		}

		public void Release(float newDuration) {
			_initialOffset = Offset;
			_duration = newDuration > 0f ? newDuration : 0f;
			_accumulated = 0f;
		}

		public void Tick(float deltaTime) {
			if (IsComplete) { return; }
			if (_duration <= 0f) { Offset = 0; return; }

			float stepsPerSecond = Math.Abs(_initialOffset) / _duration;
			_accumulated += deltaTime * stepsPerSecond;
			int steps = (int)_accumulated;
			if (steps > 0) {
				_accumulated -= steps;
				if (Offset > 0) {
					Offset = Math.Max(0, Offset - steps);
				} else if (Offset < 0) {
					Offset = Math.Min(0, Offset + steps);
				}
			}
		}
	}
}
