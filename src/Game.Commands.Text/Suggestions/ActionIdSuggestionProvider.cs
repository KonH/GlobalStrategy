using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class ActionIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public ActionIdSuggestionProvider(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.ActionIds
				.Select(id => new SuggestionItem(id, _labels.Action(id, _source.ActionNameKey(id))))
				.ToList();
		}
	}
}
