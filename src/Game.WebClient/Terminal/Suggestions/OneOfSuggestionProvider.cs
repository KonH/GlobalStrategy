using System.Collections.Generic;
using System.Linq;
using GS.Game.Commands;

namespace GS.Game.WebClient.Terminal.Suggestions {
	// OneOf suggests exactly the attribute's literal values - no config lookup, no labels
	// beyond the literal itself.
	public sealed class OneOfSuggestionProvider : ISuggestionValueProvider {
		readonly OneOfAttribute _attribute;

		public OneOfSuggestionProvider(OneOfAttribute attribute) {
			_attribute = attribute;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _attribute.Values.Select(v => new SuggestionItem(v, v)).ToList();
		}
	}
}
