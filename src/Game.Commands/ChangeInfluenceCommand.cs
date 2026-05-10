namespace GS.Game.Commands {
	public struct ChangeInfluenceCommand : ICommand {
		public string OrgId;
		public string CountryId;
		public int Delta;
	}
}
