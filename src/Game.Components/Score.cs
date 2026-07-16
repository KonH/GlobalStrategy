namespace GS.Game.Components {
	// Not [Savable] — fully derivable from province population + current ownership +
	// the scoring coefficient; recomputed at init, at load, and at each month boundary
	// by CountryScoreSystem. See ecs_patterns.md's derived-component convention.
	public struct Score {
		public double Value;
	}
}
