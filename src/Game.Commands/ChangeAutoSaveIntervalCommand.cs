namespace GS.Game.Commands {
	public record struct ChangeAutoSaveIntervalCommand(string Interval) : ICommand;
}
