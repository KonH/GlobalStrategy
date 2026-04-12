using System;

namespace GS.Game.Components {
	[Savable]
	public struct GameTime {
		public DateTime CurrentTime;
		public bool IsPaused;
		public int MultiplierIndex;
		public float AccumulatedHours;
	}
}
