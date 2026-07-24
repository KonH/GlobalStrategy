using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class OrgIdSuggestionProvider : ISuggestionValueProvider {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public OrgIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _configSource.Organization.Load().Organizations
				.Select(o => new SuggestionItem(o.OrganizationId, _localization.Get($"organization_name.{o.OrganizationId}")))
				.ToList();
		}
	}
}
