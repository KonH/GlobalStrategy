using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct CountryRelation {
		public RelationKind Kind;
		public string LeftCountryId;
		public string RightCountryId;
	}
}
