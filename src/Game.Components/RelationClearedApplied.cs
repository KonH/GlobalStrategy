using GS.Game.Common;

namespace GS.Game.Components {
	// Not [Savable] — same-tick Game Log notification, swept by
	// CleanupEffectNotificationsSystem.UpdateActionEffects, exactly like RelationSetApplied.
	public struct RelationClearedApplied {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string TargetCountryId;
		public RelationKind Kind;
	}
}
