namespace GS.Game.Commands {
	public struct PlayCountryActionCommand : ICommand {
		public string OrgId;
		public string CountryId;
		public string ActionId;
		public string TargetCharacterId;
	}
}
