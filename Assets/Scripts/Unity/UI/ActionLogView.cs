using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class ActionLogView {
		const float FadeInSeconds = 0.25f;
		const float FadeOutSeconds = 0.6f;
		const float TopGapPx = 6f;
		const float BottomReservedOffsetPx = 160f; // representative closed-state height of the bottom-bar panel
		const float WidthMultiplier = 1.5f;
		const float RightPx = 6f;

		readonly VisualElement _root;
		readonly VisualElement _content;
		readonly VisualElement _topRightPanel;
		readonly VisualElement _hudRoot;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly Dictionary<long, Label> _rendered = new();

		public ActionLogView(VisualElement hudRoot, VisualElement root, VisualElement topRightPanel,
			ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_hudRoot = hudRoot;
			_root = root;
			_topRightPanel = topRightPanel;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_content = root.Q<VisualElement>("action-log-content");
			_root.style.bottom = BottomReservedOffsetPx;
			_topRightPanel.RegisterCallback<GeometryChangedEvent>(_ => RepositionAndResize());
			RepositionAndResize();
		}

		void RepositionAndResize() {
			var hudBound = _hudRoot.worldBound;
			var trBound = _topRightPanel.worldBound;
			float width = trBound.width * WidthMultiplier;
			_root.style.width = width;
			_root.style.right = RightPx;
			_root.style.top = (trBound.yMax - hudBound.yMin) + TopGapPx;
		}

		public void Refresh(GameLogState state) {
			var currentIds = new HashSet<long>();
			foreach (var entry in state.Entries) {
				currentIds.Add(entry.SequenceId);
				if (_rendered.ContainsKey(entry.SequenceId)) { continue; }
				var label = BuildLabel(entry);
				_content.Add(label);
				_rendered[entry.SequenceId] = label;
				label.style.opacity = 0f;
				label.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
				label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeInSeconds, TimeUnit.Second) };
				label.schedule.Execute(() => label.style.opacity = 1f).ExecuteLater(20);
			}
			var toEvict = new List<long>();
			foreach (var id in _rendered.Keys) {
				if (!currentIds.Contains(id)) { toEvict.Add(id); }
			}
			foreach (var id in toEvict) {
				var label = _rendered[id];
				_rendered.Remove(id);
				label.style.transitionDuration = new List<TimeValue> { new TimeValue(FadeOutSeconds, TimeUnit.Second) };
				label.style.opacity = 0f;
				label.schedule.Execute(() => label.RemoveFromHierarchy()).ExecuteLater((long)(FadeOutSeconds * 1000));
			}
		}

		Label BuildLabel(GameLogEntry entry) {
			string text = entry.Kind switch {
				GameLogEntryKind.Discovery => BuildDiscoveryLine(entry),
				GameLogEntryKind.Control => BuildControlLine(entry),
				GameLogEntryKind.Opinion => BuildOpinionLine(entry),
				GameLogEntryKind.NewCharacter => BuildNewCharacterLine(entry),
				_ => ""
			};
			var label = new Label(text) { enableRichText = true };
			label.AddToClassList("gs-label");
			label.AddToClassList("action-log-entry");
			return label;
		}

		string BuildDiscoveryLine(GameLogEntry entry) {
			string orgName = WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
			return string.Format(_loc.Get("game_log.discovered_format"), orgName, countryName);
		}

		string BuildControlLine(GameLogEntry entry) {
			string orgName = WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
			string deltaText = "+" + FormatNumber(entry.Delta);
			string totalText = FormatNumber(entry.Total);
			return string.Format(_loc.Get("game_log.control_increased_format"), orgName, countryName, deltaText, totalText);
		}

		string BuildOpinionLine(GameLogEntry entry) {
			string orgName = WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color);
			string countryName = WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
			string roleName = $"<b>{_loc.Get($"character.role.{entry.RoleId}.name")}</b>";
			string characterName = string.Join(" ", entry.NamePartKeys.Select(_loc.Get));
			string deltaText = "+" + FormatNumber(entry.Delta);
			string totalText = FormatNumber(entry.Total);
			return string.Format(_loc.Get("game_log.opinion_increased_format"), orgName, roleName, characterName, countryName, deltaText, totalText);
		}

		string BuildNewCharacterLine(GameLogEntry entry) {
			string roleName = $"<b>{_loc.Get($"character.role.{entry.RoleId}.name")}</b>";
			string targetName = entry.IsOrgRole
				? WrapColored(_loc.Get($"organization_name.{entry.OrgId}"), _orgVisualConfig.Find(entry.OrgId)?.color)
				: WrapColored(_loc.Get($"country_name.{entry.CountryId}"), _countryVisualConfig.Find(entry.CountryId)?.color);
			string characterName = string.Join(" ", entry.NamePartKeys.Select(_loc.Get));
			return string.Format(_loc.Get("game_log.new_character_format"), roleName, targetName, characterName);
		}

		static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

		static string WrapColored(string text, Color? color) {
			string hex = ColorUtility.ToHtmlStringRGB(color ?? Color.white);
			return $"<b><color=#{hex}>{text}</color></b>";
		}
	}
}
