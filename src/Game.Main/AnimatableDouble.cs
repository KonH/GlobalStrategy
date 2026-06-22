using System.Collections.Generic;
using System.ComponentModel;

namespace GS.Main {
	public class AnimatableDouble : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		readonly List<AnimationBarrierDouble> _barriers = new List<AnimationBarrierDouble>();

		public double Actual { get; private set; }

		public double Display {
			get {
				double result = Actual;
				foreach (var b in _barriers) {
					result += b.Offset;
				}
				return result;
			}
		}

		public void SetActual(double value) {
			Actual = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public AnimationBarrierDouble Hold(double offset, float duration) {
			var barrier = new AnimationBarrierDouble(offset, duration);
			_barriers.Add(barrier);
			return barrier;
		}

		public void Cancel(AnimationBarrierDouble barrier) {
			_barriers.Remove(barrier);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public void CancelAll() {
			_barriers.Clear();
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public void Tick(float deltaTime) {
			double prevDisplay = Display;

			for (int i = _barriers.Count - 1; i >= 0; i--) {
				_barriers[i].Tick(deltaTime);
				if (_barriers[i].IsComplete) {
					_barriers.RemoveAt(i);
				}
			}

			double newDisplay = Display;
			if (newDisplay != prevDisplay) {
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
			}
		}
	}
}
