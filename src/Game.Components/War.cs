using System;
using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct War {
		[WarId] public string WarId;
		// Existing saves without DeclaredAt are incompatible (acceptable at this game stage).
		public DateTime DeclaredAt;
	}

	[Savable]
	public struct WarParticipant {
		[WarId] public string WarId;
		public WarParticipantKind Kind;
		[CountryId] public string CountryId;
	}
}
