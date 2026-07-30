using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class WarIconsView {
		sealed class RenderedButton {
			public Button Button { get; }
			public VisualElement AttackerFlag { get; }
			public VisualElement DefenderFlag { get; }
			public bool IsArmed { get; set; }

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
				_buttonsByWarId.Remove(warId);
			}

			_row.Clear();
			for (int i = 0; i < state.Entries.Count; i++) {
				WarIconEntryState entry = state.Entries[i];
				if (!_buttonsByWarId.TryGetValue(entry.WarId, out RenderedButton rendered)) {
					rendered = CreateButton(entry.WarId);
					_buttonsByWarId.Add(entry.WarId, rendered);
				}
				UpdateFlag(rendered.AttackerFlag, entry.AttackerCountryId);
				UpdateFlag(rendered.DefenderFlag, entry.DefenderCountryId);
				rendered.Button.style.marginLeft = i == 0 ? 0 : 6;
				_row.Add(rendered.Button);
			}

			_root.style.display = state.Entries.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
		}

		RenderedButton CreateButton(string warId) {
			var button = new Button();
			button.AddToClassList("gs-btn");
			button.AddToClassList("war-icon-button");

			var attackerFlag = new VisualElement {
				pickingMode = PickingMode.Ignore
			};
			attackerFlag.AddToClassList("war-icon-flag");
			button.Add(attackerFlag);

			var swords = new VisualElement {
				pickingMode = PickingMode.Ignore
			};
			swords.AddToClassList("war-icon-swords");
			button.Add(swords);

			var defenderFlag = new VisualElement {
				pickingMode = PickingMode.Ignore
			};
			defenderFlag.AddToClassList("war-icon-flag");
			button.Add(defenderFlag);

			var rendered = new RenderedButton(button, attackerFlag, defenderFlag);
			button.RegisterCallback<PointerDownEvent>(e => {
				rendered.IsArmed = e.isPrimary
					&& e.button == 0
					&& button.ContainsPoint(e.localPosition);
			});
			button.RegisterCallback<PointerUpEvent>(e => {
				bool shouldOpen = rendered.IsArmed
					&& e.isPrimary
					&& e.button == 0
					&& button.ContainsPoint(e.localPosition);
				rendered.IsArmed = false;
				if (shouldOpen) {
					_openWar?.Invoke(warId);
				}
			});
			button.RegisterCallback<PointerCancelEvent>(_ => rendered.IsArmed = false);

			_tooltip?.RegisterTrigger(
				button,
				$"war-icon:{warId}",
				_ => BuildTooltip(warId),
				new HashSet<string>());
			return rendered;
		}

		void UpdateFlag(VisualElement flag, string countryId) {
			Sprite sprite = _countryVisualConfig?.Find(countryId)?.flag;
			if (sprite == null) {
				flag.style.display = DisplayStyle.None;
				return;
			}
			flag.style.backgroundImage = new StyleBackground(sprite);
			flag.style.display = DisplayStyle.Flex;
		}

		VisualElement BuildTooltip(string warId) {
			var content = new VisualElement();
			if (!_entriesByWarId.TryGetValue(warId, out WarIconEntryState entry)) {
				return content;
			}

			string attackerName = GetCountryName(entry.AttackerCountryId);
			string defenderName = GetCountryName(entry.DefenderCountryId);
			string titleFormat = GetLocalizedFormat("hud.war.title_format", "{0} - {1} War");
			var title = new Label(string.Format(CultureInfo.InvariantCulture, titleFormat, attackerName, defenderName));
			title.AddToClassList("tooltip-header");
			content.Add(title);

			string progress = entry.Progress.ToString("G", CultureInfo.InvariantCulture);
			string progressFormat = GetLocalizedFormat("hud.war.progress_format", "Progress: {0}");
			var progressLabel = new Label(string.Format(CultureInfo.InvariantCulture, progressFormat, progress));
			progressLabel.AddToClassList("tooltip-effect-name");
			content.Add(progressLabel);
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
