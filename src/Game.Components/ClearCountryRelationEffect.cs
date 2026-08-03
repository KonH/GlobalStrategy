namespace GS.Game.Components {
	// Not [Savable] — same-tick transient marker consumed by ClearCountryRelationSystem and
	// destroyed by CleanupActionEffectsSystem on the next tick, like other one-shot action effects.
	public struct ClearCountryRelationEffect {
		public string EffectId;
		public string OrgId;
		public string CountryId;
		public string TargetCountryId;
	}
}
