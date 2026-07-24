using System.Collections.Generic;

namespace GS.Game.WebClient.Terminal.Suggestions {
	// One implementation per ParamSuggestionAttribute type (plus OneOf and the enum/none
	// fallbacks handled directly by SuggestionValueResolver) - kept small and independently
	// testable per the plan's per-provider testability requirement.
	public interface ISuggestionValueProvider {
		IReadOnlyList<SuggestionItem> GetItems();
	}
}
