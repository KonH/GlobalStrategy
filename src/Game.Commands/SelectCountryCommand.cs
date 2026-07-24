namespace GS.Game.Commands {
	public record struct SelectCountryCommand([property: CountryId] string CountryId) : ICommand;
}
