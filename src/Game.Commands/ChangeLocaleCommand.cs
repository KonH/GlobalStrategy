namespace GS.Game.Commands {
	public record struct ChangeLocaleCommand(string Locale) : ICommand;
}
