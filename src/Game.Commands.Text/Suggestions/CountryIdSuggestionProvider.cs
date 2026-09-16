using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class CountryIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public CountryIdSuggestionProvider(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.CountryIds
				.Select(id => new SuggestionItem(id, _labels.Country(id, _source.CountryDisplayName(id))))
				.ToList();
		}
	}
}
