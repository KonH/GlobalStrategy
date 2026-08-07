namespace GS.Game.Commands {
	public struct PlayCardActionCommand : ICommand {
		[ActionId] public string ActionId;
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string TargetCountryId;
		public int SlotIndex;
	}
}
