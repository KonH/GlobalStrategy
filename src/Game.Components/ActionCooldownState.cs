using System;

namespace GS.Game.Components {
	[Savable]
	public struct ActionCooldownState {
		public string OrgId;
		public string ActionId;
		public DateTime EndTime;
	}
}
