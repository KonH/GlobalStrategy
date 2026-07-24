using System;
using System.Collections.Generic;
using System.Linq;
using GS.Game.Commands;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	// Maps one CommandParameter to its value-suggestion list: attribute-driven providers first,
	// then automatic enum-name suggestion for an unannotated enum-typed parameter, then an empty
	// list for anything else (int/double/bool/plain string) - never an error.
	public class SuggestionValueResolver {
		readonly IGameConfigSource _configSource;
		readonly ILocalization _localization;

		public SuggestionValueResolver(IGameConfigSource configSource, ILocalization localization) {
			_configSource = configSource;
			_localization = localization;
		}

		public IReadOnlyList<SuggestionItem> Resolve(CommandParameter parameter) {
			switch (parameter.Suggestion) {
				case CountryIdAttribute:
					return new CountryIdSuggestionProvider(_configSource, _localization).GetItems();
				case OrgIdAttribute:
					return new OrgIdSuggestionProvider(_configSource, _localization).GetItems();
				case ProvinceIdAttribute:
					return new ProvinceIdSuggestionProvider(_configSource, _localization).GetItems();
				case ActionIdAttribute:
					return new ActionIdSuggestionProvider(_configSource, _localization).GetItems();
				case RoleIdAttribute:
					return new RoleIdSuggestionProvider(_configSource, _localization).GetItems();
				case CharacterOwnerIdAttribute:
					return new CharacterOwnerIdSuggestionProvider(_configSource, _localization).GetItems();
				case LocaleIdAttribute:
					return new LocaleIdSuggestionProvider().GetItems();
				case OneOfAttribute oneOf:
					return new OneOfSuggestionProvider(oneOf).GetItems();
				case null:
					return ResolveUnannotated(parameter.Type);
				default:
					return Array.Empty<SuggestionItem>();
			}
		}

		static IReadOnlyList<SuggestionItem> ResolveUnannotated(Type type) {
			if (!type.IsEnum) {
				return Array.Empty<SuggestionItem>();
			}
			return Enum.GetNames(type).Select(n => new SuggestionItem(n, n)).ToList();
		}
	}
}
