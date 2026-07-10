#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly VisualElement? _flagElement;
		readonly Label _controlLabel;
		readonly VisualElement? _controlRow;
		readonly VisualElement? _charsSlide;
		readonly Button? _charsToggleBtn;
		readonly VisualElement? _actionsSlide;
		readonly Button? _actionsToggleBtn;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CharactersView _charactersView;
		readonly CountryVisualConfig? _countryVisualConfig;
		readonly OrgVisualConfig? _orgVisualConfig;
		readonly TooltipSystem _tooltip;
		CountryActionsView? _actionsView;
		CountryControlState? _controlState;
		bool _charsOpen;
		bool _actionsOpen;
		string? _lastCountryId;

		public event Action<bool>? OnSubPanelOpened;
		public event Action<string, string, VisualElement>? OnCountryActionCardClicked;
		public CountryActionsView? ActionsView => _actionsView;
		public void OpenChars() => SetCharsOpen(true);

		public CountryInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig characterVisualConfig, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CountryVisualConfig? countryVisualConfig = null, OrgVisualConfig? orgVisualConfig = null) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_flagElement = root.Q("country-flag");
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_controlRow = root.Q("control-row");
			_controlLabel = root.Q<Label>("control-label");
			_charsSlide = root.Q("characters-slide");
			_charsToggleBtn = root.Q<Button>("chars-toggle-btn");
			_actionsSlide = root.Q("actions-slide");
			_actionsToggleBtn = root.Q<Button>("actions-toggle-btn");
			_loc = loc;
			_tooltip = tooltip;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig);

			if (_controlRow != null) {
				tooltip.RegisterTrigger(_controlRow, "country-control", BuildControlTooltip, new HashSet<string>());
			}
			if (_charsSlide != null) {
				_charsSlide.pickingMode = PickingMode.Ignore;
			}
			if (_actionsSlide != null) {
				_actionsSlide.pickingMode = PickingMode.Ignore;
				var actionsInstance = root.Q("actions-instance");
				if (actionsInstance != null && actionConfig != null) {
					_actionsView = new CountryActionsView(
						actionsInstance.Q("hand-container"),
						loc, actionConfig, actionVisualConfig, tooltip);
					_actionsView.OnCardClicked = (actionId, targetCharId, el) =>
						OnCountryActionCardClicked?.Invoke(actionId, targetCharId, el);
				}
			}
			if (_charsToggleBtn != null) {
				_charsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _charsToggleBtn.ContainsPoint(e.localPosition)) { ToggleChars(); }
				});
			}
			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _actionsToggleBtn.ContainsPoint(e.localPosition)) { ToggleActions(); }
				});
			}
		}

		public void Refresh(SelectedCountryState selected, CountryResourcesState resources, CountryControlState control, CountryCharactersState characters, CountryActionsState countryActions, CountryResourcesState? playerResources = null) {
			_root.style.display = selected.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (selected.IsValid) {
				_name.text = _loc.Get($"country_name.{selected.CountryId}");
				if (_flagElement != null) {
					var sprite = _countryVisualConfig?.Find(selected.CountryId)?.flag;
					if (sprite != null) {
						_flagElement.style.backgroundImage = new StyleBackground(sprite);
						_flagElement.style.display = DisplayStyle.Flex;
					} else {
						_flagElement.style.display = DisplayStyle.None;
					}
				}
			}

			if (selected.CountryId != _lastCountryId) {
				_lastCountryId = selected.CountryId;
				SetCharsOpen(false);
				SetActionsOpen(false);
			}

			bool hasChars = characters.Characters.Count > 0;
			if (_charsToggleBtn != null) {
				_charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
			}

			bool hasActions = countryActions != null && (countryActions.Hand.Count > 0 || countryActions.Deck.Count > 0);
			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
			}

			_controlState = control;
			RefreshControl(control);
			_resourcesView.Refresh(resources);
			_charactersView.Refresh(characters);
			if (!_charsOpen && _charsSlide != null) { SetPickingModeRecursive(_charsSlide, PickingMode.Ignore); }
			if (countryActions != null) {
				_actionsView?.Refresh(countryActions, playerResources ?? resources);
			}
		}

		void ToggleChars() {
			SetCharsOpen(!_charsOpen);
		}

		void ToggleActions() {
			SetActionsOpen(!_actionsOpen);
		}

		void SetCharsOpen(bool open) {
			if (open) { SetActionsOpen(false); }
			_charsOpen = open;
			if (_charsSlide != null) {
				if (open) {
					_charsSlide.AddToClassList("characters-slide--open");
					SetPickingModeRecursive(_charsSlide, PickingMode.Position);
				} else {
					_charsSlide.RemoveFromClassList("characters-slide--open");
					SetPickingModeRecursive(_charsSlide, PickingMode.Ignore);
					_tooltip?.HideAll();
				}
			}
			if (_charsToggleBtn != null) {
				var lbl = _charsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = _loc.Get("hud.org_characters"); }
				SetToggleButtonPressed(_charsToggleBtn, open);
			}
			OnSubPanelOpened?.Invoke(open);
		}

		void SetActionsOpen(bool open) {
			if (open) { SetCharsOpen(false); }
			_actionsOpen = open;
			if (_actionsSlide != null) {
				if (open) {
					_actionsSlide.AddToClassList("actions-slide--open");
					_actionsSlide.pickingMode = PickingMode.Position;
				} else {
					_actionsSlide.RemoveFromClassList("actions-slide--open");
					_actionsSlide.pickingMode = PickingMode.Ignore;
				}
			}
			if (_actionsToggleBtn != null) {
				var lbl = _actionsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = _loc.Get("hud.actions"); }
				SetToggleButtonPressed(_actionsToggleBtn, open);
			}
			OnSubPanelOpened?.Invoke(open);
		}

		static void SetToggleButtonPressed(VisualElement btn, bool pressed) {
			btn.EnableInClassList("gs-toggle-on", pressed);
			btn.EnableInClassList("gs-toggle-off", !pressed);
		}

		public void RefreshUsedControl() {
			if (_controlRow == null || _controlLabel == null) { return; }
			int pool = _controlState != null ? _controlState.PoolSize : 100;
			int used = _controlState != null ? _controlState.UsedControl.Display : 0;
			_controlLabel.text = $"{_loc.Get("hud.country_control")}: {used}/{pool}";
		}

		void RefreshControl(CountryControlState control) {
			if (_controlRow == null) {
				return;
			}
			_controlRow.style.display = DisplayStyle.Flex;
			int used = control != null ? control.UsedControl.Display : 0;
			int pool = control != null ? control.PoolSize : 100;
			_controlLabel.text = $"{_loc.Get("hud.country_control")}: {used}/{pool}";
		}

		VisualElement BuildControlTooltip(TooltipContext ctx) {
			var root = new VisualElement();

			var header = new Label(_loc.Get("hud.control_tooltip_title"));
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var control = _controlState;
			if (control == null) {
				return root;
			}

			foreach (var entry in control.OrgEntries) {
				var row = new VisualElement();
				row.AddToClassList("flag-name-row");
				row.AddToClassList("tooltip-inner-trigger");

				var flagEl = new VisualElement();
				flagEl.AddToClassList("entity-flag");
				flagEl.pickingMode = PickingMode.Ignore;
				var orgSprite = _orgVisualConfig?.Find(entry.OrgId)?.flag;
				if (orgSprite != null) {
					flagEl.style.backgroundImage = new StyleBackground(orgSprite);
					flagEl.style.display = DisplayStyle.Flex;
				} else {
					flagEl.style.display = DisplayStyle.None;
				}
				row.Add(flagEl);

				var label = new Label($"{entry.DisplayName}: {entry.Control}");
				label.AddToClassList("tooltip-effect-name");
				label.AddToClassList("tooltip-effect-positive");
				row.Add(label);

				root.Add(row);

				var capturedEntry = entry;
				ctx.RegisterInnerTrigger(row, $"org-control-{entry.OrgId}", _ =>
					BuildOrgControlInnerTooltip(capturedEntry));
			}

			return root;
		}

		VisualElement BuildOrgControlInnerTooltip(OrgControlEntry entry) {
			var root = new VisualElement();

			var header = new Label(entry.DisplayName);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var controlRow = new Label($"{_loc.Get("hud.country_control")}: {entry.Control}");
			controlRow.AddToClassList("tooltip-effect-name");
			root.Add(controlRow);

			var baseRow = new Label($"  {_loc.Get("hud.control_tooltip_base")} +{entry.BaseControl}");
			baseRow.AddToClassList("tooltip-effect-name");
			baseRow.AddToClassList("tooltip-effect-positive");
			root.Add(baseRow);

			if (entry.PermanentControl > 0) {
				var permRow = new Label($"  {_loc.Get("hud.control_tooltip_permanent")} +{entry.PermanentControl}");
				permRow.AddToClassList("tooltip-effect-name");
				permRow.AddToClassList("tooltip-effect-positive");
				root.Add(permRow);
			}

			var leadsTo = new Label(_loc.Get("hud.control_tooltip_leads_to"));
			leadsTo.AddToClassList("tooltip-effect-name");
			root.Add(leadsTo);

			var incomeRow = new Label($"  {_loc.Get("hud.control_tooltip_income")} +{entry.EstimatedMonthlyGold:F1}/month");
			incomeRow.AddToClassList("tooltip-effect-name");
			incomeRow.AddToClassList("tooltip-effect-positive");
			root.Add(incomeRow);

			return root;
		}

		static void SetPickingModeRecursive(VisualElement el, PickingMode mode) {
			el.pickingMode = mode;
			foreach (var child in el.Children()) {
				SetPickingModeRecursive(child, mode);
			}
		}
	}
}
