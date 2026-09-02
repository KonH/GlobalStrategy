using System;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Plain view for OrgInfoDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). Owns the org name/flag, the characters/actions slide toggle
	/// state and picking-mode bookkeeping, and the three sub-views (ResourcesView, OrgCharactersView,
	/// OrgActionsView) they host. The document keeps DI, tooltip ticking, event subscription and the
	/// card-play animator wiring.
	/// </summary>
	public class OrgInfoView {
		readonly ILocalization _loc;
		readonly OrgVisualConfig _orgVisualConfig;
		readonly TooltipSystem _tooltip;

		readonly Label _orgName;
		readonly VisualElement _orgFlagElement;

		readonly VisualElement _charsSlide;
		readonly Button _charsToggleBtn;
		readonly ResourcesView _resourcesView;
		readonly OrgCharactersView _charactersView;
		bool _charsOpen;

		readonly VisualElement _actionsSlide;
		readonly Button _actionsToggleBtn;
		readonly OrgActionsView _actionsView;
		bool _actionsOpen;

		public event Action<bool> OnSubPanelOpened;

		public OrgInfoView(
				VisualElement root, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig,
				CharacterVisualConfig characterVisualConfig, OrgVisualConfig orgVisualConfig,
				ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, TooltipSystem tooltip) {
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_tooltip = tooltip;

			_orgName = root.Q<Label>("org-name");
			_orgFlagElement = root.Q("org-flag");
			_charsSlide = root.Q("characters-slide");
			_charsToggleBtn = root.Q<Button>("chars-toggle-btn");
			_actionsSlide = root.Q("actions-slide");
			_actionsToggleBtn = root.Q<Button>("actions-toggle-btn");

			if (_charsSlide != null) { _charsSlide.pickingMode = PickingMode.Ignore; }
			if (_actionsSlide != null) { _actionsSlide.pickingMode = PickingMode.Ignore; }

			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_charactersView = new OrgCharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig);
			var actionsInstance = root.Q("org-actions-instance");
			if (actionsInstance != null) {
				_actionsView = new OrgActionsView(actionsInstance.Q("hand-container"), loc, actionConfig, actionVisualConfig, resourceConfig, tooltip);
			}
		}

		public Button CharsToggleBtn => _charsToggleBtn;
		public Button ActionsToggleBtn => _actionsToggleBtn;
		public OrgActionsView ActionsView => _actionsView;

		public void Refresh(PlayerOrganizationState org, bool showControls) {
			if (_orgName != null) {
				_orgName.text = org.DisplayName;
			}
			if (_orgFlagElement != null) {
				var sprite = _orgVisualConfig?.Find(org.OrgId)?.flag;
				if (sprite != null) {
					_orgFlagElement.style.backgroundImage = new StyleBackground(sprite);
					_orgFlagElement.style.display = DisplayStyle.Flex;
				} else {
					_orgFlagElement.style.display = DisplayStyle.None;
				}
			}
			_resourcesView?.Refresh(org.Resources);
			_charactersView?.Refresh(org.Characters);
			if (!_charsOpen && _charsSlide != null) { SetPickingModeRecursive(_charsSlide, PickingMode.Ignore); }
			_actionsView?.Refresh(org.Actions, org.Resources);
			if (!_actionsOpen && _actionsSlide != null) { SetPickingModeRecursive(_actionsSlide, PickingMode.Ignore); }

			bool hasChars = showControls && org.Characters.Slots.Count > 0;
			if (_charsToggleBtn != null) {
				_charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
			}

			bool hasActions = showControls && (org.Actions.Hand.Count > 0 || org.Actions.Deck.Count > 0);
			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		public void ToggleChars() {
			SetCharsOpen(!_charsOpen);
		}

		public void SetCharsOpen(bool open) {
			if (open && _actionsOpen) { SetActionsOpen(false); }
			_charsOpen = open;
			if (_charsSlide != null) {
				if (open) {
					_charsSlide.AddToClassList("org-characters-slide--open");
					SetPickingModeRecursive(_charsSlide, PickingMode.Position);
				} else {
					_charsSlide.RemoveFromClassList("org-characters-slide--open");
					SetPickingModeRecursive(_charsSlide, PickingMode.Ignore);
					_tooltip?.HideAll();
				}
			}
			if (_charsToggleBtn != null) {
				var lbl = _charsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = _loc.Get("hud.org_characters"); }
				SetToggleButtonPressed(_charsToggleBtn, open);
			}
			OnSubPanelOpened?.Invoke(_charsOpen || _actionsOpen);
		}

		public void ToggleActions() {
			SetActionsOpen(!_actionsOpen);
		}

		public void SetActionsOpen(bool open) {
			if (open && _charsOpen) { SetCharsOpen(false); }
			_actionsOpen = open;
			if (_actionsSlide != null) {
				if (open) {
					_actionsSlide.AddToClassList("org-actions-slide--open");
					SetPickingModeRecursive(_actionsSlide, PickingMode.Position);
				} else {
					_actionsSlide.RemoveFromClassList("org-actions-slide--open");
					SetPickingModeRecursive(_actionsSlide, PickingMode.Ignore);
					_tooltip?.HideAll();
				}
			}
			if (_actionsToggleBtn != null) {
				var lbl = _actionsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = _loc.Get("hud.actions"); }
				SetToggleButtonPressed(_actionsToggleBtn, open);
			}
			OnSubPanelOpened?.Invoke(_charsOpen || _actionsOpen);
		}

		static void SetToggleButtonPressed(VisualElement btn, bool pressed) {
			btn.EnableInClassList("gs-toggle-on", pressed);
			btn.EnableInClassList("gs-toggle-off", !pressed);
		}

		static void SetPickingModeRecursive(VisualElement el, PickingMode mode) {
			el.pickingMode = mode;
			foreach (var child in el.Children()) {
				SetPickingModeRecursive(child, mode);
			}
		}
	}
}
