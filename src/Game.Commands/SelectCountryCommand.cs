namespace GS.Game.Commands {
	public record struct SelectCountryCommand(string CountryId) : ICommand;
}
