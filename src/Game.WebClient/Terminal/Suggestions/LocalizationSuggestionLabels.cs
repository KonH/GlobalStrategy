using GS.Game.Commands.Text.Suggestions;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public sealed class LocalizationSuggestionLabels : ISuggestionLabels {
		readonly ILocalization _localization;

		public LocalizationSuggestionLabels(ILocalization localization) {
			_localization = localization;
		}

		public string Country(string id, string displayName) {
			return _localization.Get($"country_name.{id}");
		}

		public string Org(string id, string displayName) {
			return _localization.Get($"organization_name.{id}");
		}

		public string Province(string id, string displayName) {
			return _localization.Get($"province_name.{id}");
		}

		public string Action(string id, string nameKey) {
			return string.IsNullOrEmpty(nameKey) ? id : _localization.Get(nameKey);
		}

		public string Role(string id, string nameKey) {
			return string.IsNullOrEmpty(nameKey) ? id : _localization.Get(nameKey);
		}

		public string Raw(string id) {
			return id;
		}
	}
}
