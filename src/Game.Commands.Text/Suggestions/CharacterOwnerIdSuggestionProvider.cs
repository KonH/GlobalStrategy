using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class CharacterOwnerIdSuggestionProvider : ISuggestionValueProvider {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public CharacterOwnerIdSuggestionProvider(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> GetItems() {
			return _source.CountryOrOrgIds.Select(LabelCountryOrOrg).ToList();
		}

		SuggestionItem LabelCountryOrOrg(string id) {
			if (_source.CountryIds.Contains(id)) {
				return new SuggestionItem(id, _labels.Country(id, _source.CountryDisplayName(id)));
			}
			if (_source.OrgIds.Contains(id)) {
				return new SuggestionItem(id, _labels.Org(id, _source.OrgDisplayName(id)));
			}
			return new SuggestionItem(id, _labels.Raw(id));
		}
	}
}
