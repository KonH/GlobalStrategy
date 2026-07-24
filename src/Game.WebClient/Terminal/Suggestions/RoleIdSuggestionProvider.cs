using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class RoleIdSuggestionProvider : ISuggestionValueProvider {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public RoleIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _configSource.Character.Load().Roles
				.Select(r => new SuggestionItem(r.RoleId, _localization.Get(r.NameKey)))
				.ToList();
		}
	}
}
