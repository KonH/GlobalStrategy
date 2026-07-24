using System.Collections.Generic;
using System.Linq;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class ProvinceIdSuggestionProvider : ISuggestionValueProvider {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public ProvinceIdSuggestionProvider(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _configSource.Province.Load().Provinces
				.Select(p => new SuggestionItem(p.ProvinceId, _localization.Get($"province_name.{p.ProvinceId}")))
				.ToList();
		}
	}
}
