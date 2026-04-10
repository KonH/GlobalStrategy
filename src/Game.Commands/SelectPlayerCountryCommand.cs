namespace GS.Game.Commands {
	public record struct SelectPlayerCountryCommand(string CountryId) : ICommand;
}
