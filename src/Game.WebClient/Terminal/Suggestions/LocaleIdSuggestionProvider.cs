using System.Collections.Generic;

namespace GS.Game.WebClient.Terminal.Suggestions {
	// LocaleId is a fixed closed set - no config lookup needed, labels are just the code itself.
	public sealed class LocaleIdSuggestionProvider : ISuggestionValueProvider {
		public IReadOnlyList<SuggestionItem> GetItems() {
			return new List<SuggestionItem> {
				new("en", "en"),
				new("ru", "ru")
			};
		}
	}
}
