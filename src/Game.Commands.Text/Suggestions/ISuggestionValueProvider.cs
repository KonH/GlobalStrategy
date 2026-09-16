using System.Collections.Generic;

namespace GS.Game.Commands.Text.Suggestions {
	public interface ISuggestionValueProvider {
		IReadOnlyList<SuggestionItem> GetItems();
	}
}
