using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct CountryRelation {
		public RelationKind Kind;
		public string LeftCountryId;
		public string RightCountryId;
	}

	// Not [Savable] — same-tick transient marker consumed and destroyed by
	// SetCountryRelationSystem in the same GameLogic.Update tick it's created,
	// exactly like other one-shot action effects.
	public struct SetCountryRelationEffect {
		public string EffectId;
		public string OrgId;
		public string CountryId;
		public string TargetCountryId;
		public RelationKind Kind;
	}
}
