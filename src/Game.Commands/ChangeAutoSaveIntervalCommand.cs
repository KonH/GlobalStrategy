namespace GS.Game.Commands {
	public record struct ChangeAutoSaveIntervalCommand([property: OneOf("daily", "monthly", "yearly")] string Interval) : ICommand;
}
