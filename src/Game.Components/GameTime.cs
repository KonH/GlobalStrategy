using System;

namespace GS.Game.Components {
	public struct GameTime {
		public DateTime CurrentTime;
		public bool IsPaused;
		public int MultiplierIndex;
		public float AccumulatedHours;
	}
}
