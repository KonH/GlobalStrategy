namespace GS.Main {
	public class StaticGameLogic {
		readonly CommandAccessor _commandAccessor = new CommandAccessor();

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }

		public StaticGameLogic(string defaultLocale) {
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;
			VisualState.Locale.Set(defaultLocale);
		}

		public void Update() {
			var commands = _commandAccessor.ReadChangeLocaleCommand();
			var span = commands.AsSpan();
			if (span.Length > 0) {
				VisualState.Locale.Set(span[span.Length - 1].Locale);
			}
			_commandAccessor.Clear();
		}
	}
}
