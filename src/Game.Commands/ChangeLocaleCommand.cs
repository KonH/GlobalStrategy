namespace GS.Game.Commands {
	public record struct ChangeLocaleCommand([property: LocaleId] string Locale) : ICommand;
}
