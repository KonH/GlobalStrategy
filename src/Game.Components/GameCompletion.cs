using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct GameCompletion {
		public bool IsCompleted;
		[OrgId] public string WinnerOrganizationId;
	}
}
