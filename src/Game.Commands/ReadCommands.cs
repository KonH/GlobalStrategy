using System;

namespace GS.Game.Commands {
	public readonly struct ReadCommands<T> {
		readonly T[] _items;

		public ReadCommands(T[] items) {
			_items = items;
		}

		public void ForEach(Action<T> handler) {
			if (_items == null) return;
			foreach (var c in _items) handler(c);
		}

		public ReadOnlySpan<T> AsSpan() => _items ?? Array.Empty<T>();

		public int Count => _items?.Length ?? 0;
	}
}
