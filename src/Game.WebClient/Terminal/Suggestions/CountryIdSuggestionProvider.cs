using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class CountryIdSuggestionProvider : ISuggestionValueProvider {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public CountryIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _configSource.Country.Load().Countries
				.Select(c => new SuggestionItem(c.CountryId, _localization.Get($"country_name.{c.CountryId}")))
				.ToList();
		}
	}
}
