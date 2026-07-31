using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct War {
		public string WarId;
	}

	[Savable]
	public struct WarParticipant {
		public string WarId;
		public WarParticipantKind Kind;
		public string CountryId;
	}
}
