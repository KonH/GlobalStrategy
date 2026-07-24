namespace GS.Game.Commands {
	public struct ChangeControlCommand : ICommand {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		public int Delta;
	}
}
