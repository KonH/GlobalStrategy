namespace GS.Game.Commands {
	public struct DebugDiscardCardCommand : ICommand {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[ActionId] public string ActionId;
		[CountryId] public string TargetCountryId;
		public int SlotIndex;
	}
}
