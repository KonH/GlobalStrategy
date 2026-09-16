using System.Collections.Generic;
using System.Linq;
using GS.Game.Common;

namespace GS.Game.Commands.Text.Suggestions {
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
