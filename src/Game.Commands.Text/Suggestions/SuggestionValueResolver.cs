using System;
using System.Collections.Generic;
using System.Linq;
using GS.Game.Common;

namespace GS.Game.Commands.Text.Suggestions {
	public class SuggestionValueResolver {
		readonly ISuggestionSource _source;
		readonly ISuggestionLabels _labels;

		public SuggestionValueResolver(ISuggestionSource source, ISuggestionLabels labels) {
			_source = source;
			_labels = labels;
		}

		public IReadOnlyList<SuggestionItem> Resolve(CommandParameter parameter) {
			switch (parameter.Suggestion) {
				case CountryIdAttribute:
					return new CountryIdSuggestionProvider(_source, _labels).GetItems();
				case OrgIdAttribute:
					return new OrgIdSuggestionProvider(_source, _labels).GetItems();
				case ProvinceIdAttribute:
					return new ProvinceIdSuggestionProvider(_source, _labels).GetItems();
				case ActionIdAttribute:
					return new ActionIdSuggestionProvider(_source, _labels).GetItems();
				case RoleIdAttribute:
					return new RoleIdSuggestionProvider(_source, _labels).GetItems();
				case CharacterOwnerIdAttribute:
					return new CharacterOwnerIdSuggestionProvider(_source, _labels).GetItems();
				case LocaleIdAttribute:
					return new LocaleIdSuggestionProvider(_source).GetItems();
				case ResourceIdAttribute:
					return new RawIdSuggestionProvider(_source.ResourceIds, _labels).GetItems();
				case CharacterIdAttribute:
					return new RawIdSuggestionProvider(_source.CharacterIds, _labels).GetItems();
				case WarIdAttribute:
					return new RawIdSuggestionProvider(_source.WarIds, _labels).GetItems();
				case BattleIdAttribute:
					return new RawIdSuggestionProvider(_source.BattleIds, _labels).GetItems();
				case EffectIdAttribute:
					return new RawIdSuggestionProvider(_source.EffectIds, _labels).GetItems();
				case TaskIdAttribute:
					return new RawIdSuggestionProvider(_source.TaskIds, _labels).GetItems();
				case CollectorIdAttribute:
					return new RawIdSuggestionProvider(_source.CollectorIds, _labels).GetItems();
				case OwnerIdAttribute:
					return new RawIdSuggestionProvider(_source.OwnerUnionIds, _labels).GetItems();
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
