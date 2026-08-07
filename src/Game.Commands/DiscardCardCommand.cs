namespace GS.Game.Commands {
	public struct DiscardCardCommand : ICommand {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[ActionId] public string ActionId;
		[CountryId] public string TargetCountryId;
		public int SlotIndex;
	}
}
