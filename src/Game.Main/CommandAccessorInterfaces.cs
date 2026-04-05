using GS.Game.Commands;

namespace GS.Main {
	public interface IWriteOnlyCommandAccessor {
		void Push<TCommand>(TCommand cmd) where TCommand : ICommand;
	}

	public interface IReadOnlyCommandAccessor {
		ReadCommands<TCommand> Read<TCommand>() where TCommand : ICommand;
	}
}
