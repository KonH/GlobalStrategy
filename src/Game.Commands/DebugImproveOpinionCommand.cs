namespace GS.Game.Commands {
	public struct DebugImproveOpinionCommand : ICommand {
		[CountryId] public string CountryId;
		[OrgId] public string OrgId;
	}
}
