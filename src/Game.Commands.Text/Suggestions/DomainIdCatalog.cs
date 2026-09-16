using System;
using System.Collections.Generic;

namespace GS.Game.Commands.Text.Suggestions {
	public static class DomainIdCatalog {
		public static IReadOnlyList<string> IdsFor(ISuggestionSource source, string? domainIdKind, string? ownerType) {
			if (source == null || string.IsNullOrEmpty(domainIdKind)) {
				return Array.Empty<string>();
			}
			switch (domainIdKind) {
				case "CountryId": return source.CountryIds;
				case "OrgId": return source.OrgIds;
				case "ProvinceId": return source.ProvinceIds;
				case "ActionId": return source.ActionIds;
				case "RoleId": return source.RoleIds;
				case "LocaleId": return source.LocaleIds;
				case "ResourceId": return source.ResourceIds;
				case "CharacterId": return source.CharacterIds;
				case "WarId": return source.WarIds;
				case "BattleId": return source.BattleIds;
				case "EffectId": return source.EffectIds;
				case "TaskId": return source.TaskIds;
				case "CollectorId": return source.CollectorIds;
				case "CharacterOwnerId": return source.CountryOrOrgIds;
				case "OwnerId":
					if (string.Equals(ownerType, "Country", StringComparison.OrdinalIgnoreCase)) {
						return source.CountryIds;
					}
					if (string.Equals(ownerType, "Org", StringComparison.OrdinalIgnoreCase)) {
						return source.OrgIds;
					}
					if (string.Equals(ownerType, "Character", StringComparison.OrdinalIgnoreCase)) {
						return source.CharacterIds;
					}
					if (string.Equals(ownerType, "Province", StringComparison.OrdinalIgnoreCase)) {
						return source.ProvinceIds;
					}
					if (string.Equals(ownerType, "War", StringComparison.OrdinalIgnoreCase)) {
						return source.WarIds;
					}
					return source.OwnerUnionIds;
				default:
					return Array.Empty<string>();
			}
		}

		public static Dictionary<string, IReadOnlyList<string>> All(ISuggestionSource source) {
			var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			if (source == null) {
				return result;
			}
			string[] kinds = {
				"CountryId", "OrgId", "ProvinceId", "ActionId", "RoleId", "LocaleId",
				"ResourceId", "CharacterId", "WarId", "BattleId", "EffectId", "TaskId",
				"CollectorId", "CharacterOwnerId", "OwnerId"
			};
			foreach (string kind in kinds) {
				result[kind] = IdsFor(source, kind, null);
			}
			result["OwnerId:Country"] = IdsFor(source, "OwnerId", "Country");
			result["OwnerId:Org"] = IdsFor(source, "OwnerId", "Org");
			result["OwnerId:Character"] = IdsFor(source, "OwnerId", "Character");
			result["OwnerId:Province"] = IdsFor(source, "OwnerId", "Province");
			result["OwnerId:War"] = IdsFor(source, "OwnerId", "War");
			return result;
		}

		public static string CacheKey(string? domainIdKind, string? ownerType) {
			if (string.Equals(domainIdKind, "OwnerId", StringComparison.Ordinal)
				&& !string.IsNullOrEmpty(ownerType)) {
				return "OwnerId:" + ownerType;
			}
			return domainIdKind ?? "";
		}
	}
}
