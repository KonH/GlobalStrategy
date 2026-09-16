using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class RawIdSuggestionProvider : ISuggestionValueProvider {
		readonly IReadOnlyList<string> _ids;
		readonly ISuggestionLabels _labels;

		public RawIdSuggestionProvider(IReadOnlyList<string> ids, ISuggestionLabels labels) {
			_ids = ids;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _ids.Select(id => new SuggestionItem(id, _labels.Raw(id))).ToList();
		}
	}
}
