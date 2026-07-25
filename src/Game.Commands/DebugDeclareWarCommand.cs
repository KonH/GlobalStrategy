namespace GS.Game.Commands {
	public struct DebugDeclareWarCommand : ICommand {
		[CountryId] public string AttackerCountryId;
		[CountryId] public string DefenderCountryId;
	}
}
