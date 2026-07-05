namespace GS.Game.Commands {
	public record struct SaveGameCommand(bool IsAutoSave = false) : ICommand;
}
