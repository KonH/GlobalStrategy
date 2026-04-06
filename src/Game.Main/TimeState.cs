using System;
using System.ComponentModel;

namespace GS.Main {
	public class TimeState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public DateTime CurrentTime { get; private set; }
		public bool IsPaused { get; private set; }
		public int MultiplierIndex { get; private set; }

		public void Set(DateTime time, bool paused, int multiplierIndex) {
			CurrentTime = time;
			IsPaused = paused;
			MultiplierIndex = multiplierIndex;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}
}
