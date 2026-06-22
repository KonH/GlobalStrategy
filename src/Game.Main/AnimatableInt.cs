using System.Collections.Generic;
using System.ComponentModel;

namespace GS.Main {
	public class AnimatableInt : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		readonly List<AnimationBarrierInt> _barriers = new List<AnimationBarrierInt>();

		public int Actual { get; private set; }

		public int Display {
			get {
				int result = Actual;
				foreach (var b in _barriers) {
					result += b.Offset;
				}
				return result;
			}
		}

		public void SetActual(int value) {
			Actual = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public AnimationBarrierInt Hold(int offset, float duration) {
			var barrier = new AnimationBarrierInt(offset, duration);
			_barriers.Add(barrier);
			return barrier;
		}

		public void Cancel(AnimationBarrierInt barrier) {
			_barriers.Remove(barrier);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public void CancelAll() {
			_barriers.Clear();
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public void Tick(float deltaTime) {
			int prevDisplay = Display;

			for (int i = _barriers.Count - 1; i >= 0; i--) {
				_barriers[i].Tick(deltaTime);
				if (_barriers[i].IsComplete) {
					_barriers.RemoveAt(i);
				}
			}

			int newDisplay = Display;
			if (newDisplay != prevDisplay) {
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
			}
		}
	}
}
