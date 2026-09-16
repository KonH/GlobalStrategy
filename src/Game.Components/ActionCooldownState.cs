using System;
using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ActionCooldownState {
		[OrgId] public string OrgId;
		[ActionId] public string ActionId;
		public DateTime EndTime;
	}
}
