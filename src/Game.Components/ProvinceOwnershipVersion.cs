namespace GS.Game.Components {
	// Not [Savable] — a change counter for VisualStateConverter's dirty-check, not
	// domain state. If it resets on load, the worst case is one redundant rebuild.
	public struct ProvinceOwnershipVersion {
		public int Value;
	}
}
