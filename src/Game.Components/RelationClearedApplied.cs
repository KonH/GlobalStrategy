using GS.Game.Common;

namespace GS.Game.Components {
	// Not [Savable] — same-tick Game Log notification, swept by
	// CleanupEffectNotificationsSystem.UpdateActionEffects, exactly like RelationSetApplied.
	public struct RelationClearedApplied {
		public string OrgId;
		public string CountryId;
		public string TargetCountryId;
		public RelationKind Kind;
	}
}
