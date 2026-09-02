using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class WarIconsView {
		sealed class RenderedButton {
			public Button Button { get; }
			public VisualElement AttackerFlag { get; }
			public VisualElement DefenderFlag { get; }

			public RenderedButton(Button button, VisualElement attackerFlag, VisualElement defenderFlag) {
				Button = button;
				AttackerFlag = attackerFlag;
				DefenderFlag = defenderFlag;
			}
		}

		readonly VisualElement _root;
		readonly VisualElement _row;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly TooltipSystem _tooltip;
		readonly Action<string> _openWar;
		readonly Dictionary<string, WarIconEntryState> _entriesByWarId = new(StringComparer.Ordinal);
		readonly Dictionary<string, RenderedButton> _buttonsByWarId = new(StringComparer.Ordinal);

		public WarIconsView(VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig,
			TooltipSystem tooltip, Action<string> openWar) {
			_root = root;
			_row = root?.Q("war-icons-row");
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_tooltip = tooltip;
			_openWar = openWar;
		}

		public void Refresh(WarIconsState state) {
			if (_root == null || _row == null || state == null) {
				return;
			}

			_entriesByWarId.Clear();
			foreach (WarIconEntryState entry in state.Entries) {
				_entriesByWarId[entry.WarId] = entry;
			}

			var removedIds = new List<string>();
			foreach (string warId in _buttonsByWarId.Keys) {
				if (!_entriesByWarId.ContainsKey(warId)) {
					removedIds.Add(warId);
				}
			}
			foreach (string warId in removedIds) {
				_buttonsByWarId[warId].Button.RemoveFromHierarchy();
				_buttonsByWarId.Remove(warId);
			}

			for (int i = 0; i < state.Entries.Count; i++) {
				WarIconEntryState entry = state.Entries[i];
				if (!_buttonsByWarId.TryGetValue(entry.WarId, out RenderedButton rendered)) {
					rendered = CreateButton(entry.WarId);
					_buttonsByWarId.Add(entry.WarId, rendered);
				}
				UpdateFlag(rendered.AttackerFlag, entry.AttackerCountryId);
				UpdateFlag(rendered.DefenderFlag, entry.DefenderCountryId);
				rendered.Button.style.marginLeft = i == 0 ? 0 : 6;
				if (_row.IndexOf(rendered.Button) != i) {
					_row.Insert(i, rendered.Button);
				}
			}

			_root.style.display = state.Entries.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
		}

		RenderedButton CreateButton(string warId) {
			var button = new Button();
			button.AddToClassList("gs-btn");
			button.AddToClassList("war-icon-button");

			var attackerFlag = FlagBadgeBuilder.Build("war-icon-flag");
			button.Add(attackerFlag);

			var swords = new VisualElement {
				pickingMode = PickingMode.Ignore
			};
			swords.AddToClassList("war-icon-swords");
			button.Add(swords);

			var defenderFlag = FlagBadgeBuilder.Build("war-icon-flag");
			button.Add(defenderFlag);

			var rendered = new RenderedButton(button, attackerFlag, defenderFlag);
			button.OnClick(() => _openWar?.Invoke(warId));

			_tooltip?.RegisterTrigger(
				button,
				$"war-icon:{warId}",
				_ => BuildTooltip(warId),
				new HashSet<string>());
			return rendered;
		}

		void UpdateFlag(VisualElement flag, string countryId) {
			Sprite sprite = _countryVisualConfig?.Find(countryId)?.flag;
			FlagBadgeBuilder.Bind(flag, sprite);
		}

		VisualElement BuildTooltip(string warId) {
			var content = TooltipBodyBuilder.NewRoot();
			if (!_entriesByWarId.TryGetValue(warId, out WarIconEntryState entry)) {
				return content;
			}

			string attackerName = GetCountryName(entry.AttackerCountryId);
			string defenderName = GetCountryName(entry.DefenderCountryId);
			string titleFormat = GetLocalizedFormat("hud.war.title_format", "{0} - {1} War");
			TooltipBodyBuilder.AddHeader(content, string.Format(CultureInfo.InvariantCulture, titleFormat, attackerName, defenderName));

			string progress = entry.Progress.ToString("G", CultureInfo.InvariantCulture);
			string progressFormat = GetLocalizedFormat("hud.war.progress_format", "Progress: {0}");
			TooltipBodyBuilder.AddLine(content, string.Format(CultureInfo.InvariantCulture, progressFormat, progress));
			return content;
		}

		string GetCountryName(string countryId) {
			string key = $"country_name.{countryId}";
			string localized = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(localized) || localized == key ? countryId : localized;
		}

		string GetLocalizedFormat(string key, string fallback) {
			string localized = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(localized) || localized == key ? fallback : localized;
		}
	}
}
