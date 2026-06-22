using System;

namespace GS.Main {
	public class AnimationBarrierInt {
		int _initialOffset;
		float _duration;
		float _accumulated;
		bool _started;

		public int Offset { get; private set; }
		public bool IsComplete => _started && Offset == 0;

		internal AnimationBarrierInt(int offset) {
			_initialOffset = offset;
			Offset = offset;
		}

		public void Release(float duration) {
			_initialOffset = Offset;
			_duration = duration > 0f ? duration : 0f;
			_accumulated = 0f;
			_started = true;
		}

		public void Tick(float deltaTime) {
			if (!_started || IsComplete) { return; }
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
