using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class RoleIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public RoleIdSuggestionProvider(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.RoleIds
				.Select(id => new SuggestionItem(id, _labels.Role(id, _source.RoleNameKey(id))))
				.ToList();
		}
	}
}
