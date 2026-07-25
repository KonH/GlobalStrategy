namespace GS.Game.Components {
	// Not [Savable] — a monotonic dirty-counter, not domain state. If it resets on load, the
	// worst case is one harmless resync.
	public struct CountryRelationsVersion {
		public int Value;
	}
}
