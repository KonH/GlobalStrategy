namespace GS.Game.Commands {
	public struct ChangeControlCommand : ICommand {
		public string OrgId;
		public string CountryId;
		public int Delta;
	}
}
