using System.Globalization;
using System.Linq;
using UnityEngine;
using GS.Game.Common;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	static class GameLogLineFormatter {
		public static string BuildDiscoveryLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string orgName = WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			return string.Format(loc.Get("game_log.discovered_format"), orgName, countryName);
		}

		public static string BuildControlLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string orgName = WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			string totalText = FormatNumber(entry.Total);
			if (entry.Delta >= 0) {
				string deltaText = "+" + FormatNumber(entry.Delta);
				return string.Format(loc.Get("game_log.control_increased_format"), orgName, countryName, deltaText, totalText);
			}
			string decreaseText = "-" + FormatNumber(System.Math.Abs(entry.Delta));
			return string.Format(loc.Get("game_log.control_decreased_format"), orgName, countryName, decreaseText, totalText);
		}

		public static string BuildOpinionLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string orgName = WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			string roleName = $"<b>{loc.Get($"character.role.{entry.RoleId}.name")}</b>";
			string characterName = string.Join(" ", entry.NamePartKeys.Select(loc.Get));
			string deltaText = "+" + FormatNumber(entry.Delta);
			string totalText = FormatNumber(entry.Total);
			return string.Format(loc.Get("game_log.opinion_increased_format"), orgName, roleName, characterName, countryName, deltaText, totalText);
		}

		public static string BuildRelationLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string orgName = WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			string targetCountryName = WrapColored(loc.Get($"country_name.{entry.TargetCountryId}"), countryVisualConfig.Find(entry.TargetCountryId)?.color);
			string formatKey = entry.RelationKind == RelationKind.Friend ? "game_log.relation_friend_format" : "game_log.relation_rival_format";
			return string.Format(loc.Get(formatKey), orgName, countryName, targetCountryName);
		}

		public static string BuildWarResolvedLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string winnerName = WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			string loserName = WrapColored(loc.Get($"country_name.{entry.TargetCountryId}"), countryVisualConfig.Find(entry.TargetCountryId)?.color);
			return string.Format(loc.Get("game_log.war_resolved_format"), winnerName, loserName);
		}

		public static string BuildNewCharacterLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			string roleName = $"<b>{loc.Get($"character.role.{entry.RoleId}.name")}</b>";
			string targetName = entry.IsOrgRole
				? WrapColored(loc.Get($"organization_name.{entry.OrgId}"), orgVisualConfig.Find(entry.OrgId)?.color)
				: WrapColored(loc.Get($"country_name.{entry.CountryId}"), countryVisualConfig.Find(entry.CountryId)?.color);
			string characterName = string.Join(" ", entry.NamePartKeys.Select(loc.Get));
			return string.Format(loc.Get("game_log.new_character_format"), roleName, targetName, characterName);
		}

		public static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

		public static string WrapColored(string text, Color? color) {
			string hex = ColorUtility.ToHtmlStringRGB(color ?? Color.white);
			return $"<b><color=#{hex}>{text}</color></b>";
		}
	}
}
