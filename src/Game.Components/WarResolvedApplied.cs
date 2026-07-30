namespace GS.Game.Components {
	// Not [Savable] — same-tick Game Log notification, swept by
	// CleanupEffectNotificationsSystem.UpdateActionEffects, exactly like RelationClearedApplied.
	public struct WarResolvedApplied {
		public string WarId;
		public string WinnerCountryId;
		public string LoserCountryId;
	}
}
