using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct Organization {
		[OrgId] public string OrganizationId;
		public string DisplayName;
	}
}
