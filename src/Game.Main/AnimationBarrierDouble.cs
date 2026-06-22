namespace GS.Main {
	public class AnimationBarrierDouble {
		double _initialOffset;
		float _duration;
		float _elapsed;
		bool _started;

		public double Offset { get; private set; }
		public bool IsComplete => _started && _elapsed >= _duration;

		internal AnimationBarrierDouble(double offset) {
			_initialOffset = offset;
			Offset = offset;
		}

		public void Release(float duration) {
			_initialOffset = Offset;
			_elapsed = 0f;
			_duration = duration > 0f ? duration : 0f;
			_started = true;
		}

		public void Tick(float deltaTime) {
			if (!_started || IsComplete) { return; }
			_elapsed += deltaTime;
			float t = _duration > 0f ? (_elapsed / _duration) : 1f;
			if (t > 1f) { t = 1f; }
			Offset = _initialOffset * (1.0 - t);
			if (t >= 1f) { Offset = 0.0; }
		}
	}
}
