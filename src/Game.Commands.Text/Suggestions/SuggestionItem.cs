namespace GS.Game.Commands.Text.Suggestions {
	public sealed class SuggestionItem {
		public string Value { get; }
		public string Label { get; }

		public SuggestionItem(string value, string label) {
			Value = value;
			Label = label;
		}
	}
}
