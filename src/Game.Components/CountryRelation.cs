using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct CountryRelation {
		public RelationKind Kind;
		[CountryId] public string LeftCountryId;
		[CountryId] public string RightCountryId;
	}

	// Not [Savable] — same-tick transient marker consumed and destroyed by
	// SetCountryRelationSystem in the same GameLogic.Update tick it's created,
	// exactly like other one-shot action effects.
	public struct SetCountryRelationEffect {
		[EffectId] public string EffectId;
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string TargetCountryId;
		public RelationKind Kind;
	}
}
