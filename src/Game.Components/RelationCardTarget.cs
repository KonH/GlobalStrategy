using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct RelationCardTarget {
		[CountryId] public string TargetCountryId;
		public RelationKind Kind;
	}
}
