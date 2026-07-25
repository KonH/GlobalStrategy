using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct RelationCardTarget {
		public string TargetCountryId;
		public RelationKind Kind;
	}
}
