using System;
using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct War {
		public string WarId;
		// Existing saves without DeclaredAt are incompatible (acceptable at this game stage).
		public DateTime DeclaredAt;
	}

	[Savable]
	public struct WarProgress {
		public double Value;
	}

	[Savable]
	public struct WarParticipant {
		public string WarId;
		public WarParticipantKind Kind;
		public string CountryId;
	}
}
