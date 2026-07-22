namespace GS.Game.Components {
	public enum OrganizationGameResult {
		InProgress,
		Winner,
		Loser
	}

	[Savable]
	public struct OrganizationGameOutcome {
		public int ParticipationOrder;
		public OrganizationGameResult Result;
	}
}
