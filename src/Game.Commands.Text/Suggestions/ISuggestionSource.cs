using System.Collections.Generic;

namespace GS.Game.Commands.Text.Suggestions {
	public interface ISuggestionSource {
		IReadOnlyList<string> CountryIds { get; }
		IReadOnlyList<string> OrgIds { get; }
		IReadOnlyList<string> ProvinceIds { get; }
		IReadOnlyList<string> ActionIds { get; }
		IReadOnlyList<string> RoleIds { get; }
		IReadOnlyList<string> LocaleIds { get; }
		IReadOnlyList<string> ResourceIds { get; }
		IReadOnlyList<string> CharacterIds { get; }
		IReadOnlyList<string> WarIds { get; }
		IReadOnlyList<string> BattleIds { get; }
		IReadOnlyList<string> EffectIds { get; }
		IReadOnlyList<string> TaskIds { get; }
		IReadOnlyList<string> CollectorIds { get; }
		IReadOnlyList<string> CountryOrOrgIds { get; }
		IReadOnlyList<string> OwnerUnionIds { get; }

		string CountryDisplayName(string id);
		string OrgDisplayName(string id);
		string ProvinceDisplayName(string id);
		string ActionNameKey(string id);
		string RoleNameKey(string id);
	}
}
