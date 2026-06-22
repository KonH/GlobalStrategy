namespace GS.Main {
	public class AnimationBarrierDouble {
		double _initialOffset;
		float _duration;
		float _elapsed;

		public double Offset { get; private set; }
		public bool IsComplete => _elapsed >= _duration || (_duration <= 0f && Offset == 0.0);

		internal AnimationBarrierDouble(double offset, float duration) {
			_initialOffset = offset;
			Offset = offset;
			_duration = duration;
			_elapsed = 0f;
		}

		public void Release(float newDuration) {
			_initialOffset = Offset;
			_elapsed = 0f;
			_duration = newDuration > 0f ? newDuration : 0f;
		}

		public void Tick(float deltaTime) {
			if (IsComplete) { return; }
			_elapsed += deltaTime;
			float t = _duration > 0f ? (_elapsed / _duration) : 1f;
			if (t > 1f) { t = 1f; }
			Offset = _initialOffset * (1.0 - t);
			if (t >= 1f) { Offset = 0.0; }
		}
	}
}
