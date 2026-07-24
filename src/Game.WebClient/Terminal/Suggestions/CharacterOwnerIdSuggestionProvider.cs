using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	// CharacterOwnerId is a union of country ids and org ids - both a country and an org can
	// act as a character owner (DebugCycleCharacterCommand.OwnerId/DebugDropCharacterCommand.OwnerId).
	public sealed class CharacterOwnerIdSuggestionProvider : ISuggestionValueProvider {
		readonly CountryIdSuggestionProvider _countryProvider;
		readonly OrgIdSuggestionProvider _orgProvider;

		public CharacterOwnerIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_countryProvider = new CountryIdSuggestionProvider(configSource, localization);
			_orgProvider = new OrgIdSuggestionProvider(configSource, localization);
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _countryProvider.GetItems().Concat(_orgProvider.GetItems()).ToList();
		}
	}
}
