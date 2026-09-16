using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class OrgIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public OrgIdSuggestionProvider(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.OrgIds
				.Select(id => new SuggestionItem(id, _labels.Org(id, _source.OrgDisplayName(id))))
				.ToList();
		}
	}
}
