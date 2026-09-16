using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class LocaleIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;

		public LocaleIdSuggestionProvider(ISuggestionSource source) {
			_source = source;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.LocaleIds.Select(id => new SuggestionItem(id, id)).ToList();
		}
	}
}
