using System;
using System.Collections.Generic;
using System.Linq;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class ConfigCatalogSuggestionSource : ISuggestionSource {
		readonly Dictionary<string, string> _countryNames;
		readonly Dictionary<string, string> _orgNames;
		readonly Dictionary<string, string> _provinceNames;
		readonly Dictionary<string, string> _actionNameKeys;
		readonly Dictionary<string, string> _roleNameKeys;

		public IReadOnlyList<string> CountryIds { get; }
		public IReadOnlyList<string> OrgIds { get; }
		public IReadOnlyList<string> ProvinceIds { get; }
		public IReadOnlyList<string> ActionIds { get; }
		public IReadOnlyList<string> RoleIds { get; }
		public IReadOnlyList<string> LocaleIds { get; } = new[] { "en", "ru" };
		public IReadOnlyList<string> ResourceIds { get; }
		public IReadOnlyList<string> CharacterIds { get; }
		public IReadOnlyList<string> WarIds { get; }
		public IReadOnlyList<string> BattleIds { get; }
		public IReadOnlyList<string> EffectIds { get; }
		public IReadOnlyList<string> TaskIds { get; }
		public IReadOnlyList<string> CollectorIds { get; }
		public IReadOnlyList<string> CountryOrOrgIds { get; }
		public IReadOnlyList<string> OwnerUnionIds { get; }

		public ConfigCatalogSuggestionSource(
			IReadOnlyList<string> countryIds,
			IReadOnlyList<string> orgIds,
			IReadOnlyList<string> provinceIds,
			IReadOnlyList<string> actionIds,
			IReadOnlyList<string> roleIds,
			IReadOnlyList<string> resourceIds,
			IReadOnlyList<string> characterIds,
			IReadOnlyList<string> warIds,
			IReadOnlyList<string> battleIds,
			IReadOnlyList<string> effectIds,
			IReadOnlyList<string> taskIds,
			IReadOnlyList<string> collectorIds,
			IReadOnlyList<string>? ownerUnionIds = null,
			IReadOnlyDictionary<string, string>? countryDisplayNames = null,
			IReadOnlyDictionary<string, string>? orgDisplayNames = null,
			IReadOnlyDictionary<string, string>? provinceDisplayNames = null,
			IReadOnlyDictionary<string, string>? actionNameKeys = null,
			IReadOnlyDictionary<string, string>? roleNameKeys = null
		) {
			CountryIds = DistinctSorted(countryIds);
			OrgIds = DistinctSorted(orgIds);
			ProvinceIds = DistinctSorted(provinceIds);
			ActionIds = DistinctSorted(actionIds);
			RoleIds = DistinctSorted(roleIds);
			ResourceIds = DistinctSorted(resourceIds);
			CharacterIds = DistinctSorted(characterIds);
			WarIds = DistinctSorted(warIds);
			BattleIds = DistinctSorted(battleIds);
			EffectIds = DistinctSorted(effectIds);
			TaskIds = DistinctSorted(taskIds);
			CollectorIds = DistinctSorted(collectorIds);
			CountryOrOrgIds = DistinctSorted(CountryIds.Concat(OrgIds).ToList());
			OwnerUnionIds = DistinctSorted(ownerUnionIds ?? CountryOrOrgIds);
			_countryNames = Copy(countryDisplayNames);
			_orgNames = Copy(orgDisplayNames);
			_provinceNames = Copy(provinceDisplayNames);
			_actionNameKeys = Copy(actionNameKeys);
			_roleNameKeys = Copy(roleNameKeys);
		}

		public string CountryDisplayName(string id) => Lookup(_countryNames, id);
		public string OrgDisplayName(string id) => Lookup(_orgNames, id);
		public string ProvinceDisplayName(string id) => Lookup(_provinceNames, id);
		public string ActionNameKey(string id) => Lookup(_actionNameKeys, id);
		public string RoleNameKey(string id) => Lookup(_roleNameKeys, id);

		static List<string> DistinctSorted(IReadOnlyList<string> ids) {
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (string id in ids) {
				if (!string.IsNullOrEmpty(id)) {
					set.Add(id);
				}
			}
			var list = set.ToList();
			list.Sort(StringComparer.Ordinal);
			return list;
		}

		static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string>? source) {
			return source == null
				? new Dictionary<string, string>(StringComparer.Ordinal)
				: new Dictionary<string, string>(source, StringComparer.Ordinal);
		}

		static string Lookup(Dictionary<string, string> map, string id) {
			return map.TryGetValue(id, out string? value) ? value : "";
		}
	}
}
