using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class ActionIdSuggestionProvider : ISuggestionValueProvider {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public ActionIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _configSource.Action.Load().Actions
				.Select(a => new SuggestionItem(a.ActionId, _localization.Get(a.NameKey)))
				.ToList();
		}
	}
}
