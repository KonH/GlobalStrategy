using System;
using System.Collections.Generic;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class DelegatingSuggestionSource : ISuggestionSource {
		readonly Func<ISuggestionSource> _inner;

		public DelegatingSuggestionSource(Func<ISuggestionSource> inner) {
			_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		}

		ISuggestionSource Inner => _inner();

		public IReadOnlyList<string> CountryIds => Inner.CountryIds;
		public IReadOnlyList<string> OrgIds => Inner.OrgIds;
		public IReadOnlyList<string> ProvinceIds => Inner.ProvinceIds;
		public IReadOnlyList<string> ActionIds => Inner.ActionIds;
		public IReadOnlyList<string> RoleIds => Inner.RoleIds;
		public IReadOnlyList<string> LocaleIds => Inner.LocaleIds;
		public IReadOnlyList<string> ResourceIds => Inner.ResourceIds;
		public IReadOnlyList<string> CharacterIds => Inner.CharacterIds;
		public IReadOnlyList<string> WarIds => Inner.WarIds;
		public IReadOnlyList<string> BattleIds => Inner.BattleIds;
		public IReadOnlyList<string> EffectIds => Inner.EffectIds;
		public IReadOnlyList<string> TaskIds => Inner.TaskIds;
		public IReadOnlyList<string> CollectorIds => Inner.CollectorIds;
		public IReadOnlyList<string> CountryOrOrgIds => Inner.CountryOrOrgIds;
		public IReadOnlyList<string> OwnerUnionIds => Inner.OwnerUnionIds;

		public string CountryDisplayName(string id) => Inner.CountryDisplayName(id);
		public string OrgDisplayName(string id) => Inner.OrgDisplayName(id);
		public string ProvinceDisplayName(string id) => Inner.ProvinceDisplayName(id);
		public string ActionNameKey(string id) => Inner.ActionNameKey(id);
		public string RoleNameKey(string id) => Inner.RoleNameKey(id);
	}
}
