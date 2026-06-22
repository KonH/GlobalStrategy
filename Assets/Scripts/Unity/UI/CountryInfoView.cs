using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly Label _influenceLabel;
		readonly VisualElement _influenceRow;
		readonly VisualElement _charsSlide;
		readonly Button _charsToggleBtn;
		readonly VisualElement _actionsSlide;
		readonly Button _actionsToggleBtn;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CharactersView _charactersView;
		CountryActionsView _actionsView;
		CountryInfluenceState _influenceState;
		bool _charsOpen;
		bool _actionsOpen;
		string _lastCountryId;

		public event Action<bool> OnSubPanelOpened;
		public event Action<string, string, VisualElement> OnCountryActionCardClicked;
		public CountryActionsView ActionsView => _actionsView;
		public void OpenChars() => SetCharsOpen(true);

		public CountryInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig characterVisualConfig, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, Dictionary<string, AnimatableInt>? characterOpinions = null) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_influenceRow = root.Q("influence-row");
			_influenceLabel = root.Q<Label>("influence-label");
			_charsSlide = root.Q("characters-slide");
			_charsToggleBtn = root.Q<Button>("chars-toggle-btn");
			_actionsSlide = root.Q("actions-slide");
			_actionsToggleBtn = root.Q<Button>("actions-toggle-btn");
			_loc = loc;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig, characterOpinions);

			if (_influenceRow != null) {
				tooltip.RegisterTrigger(_influenceRow, "country-influence", BuildInfluenceTooltip, new HashSet<string>());
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

		public void Refresh(SelectedCountryState selected, PlayerCountryState player, CountryResourcesState resources, CountryInfluenceState influence, CountryCharactersState characters, CountryActionsState countryActions, CountryResourcesState playerResources = null) {
			_root.style.display = selected.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (selected.IsValid) {
				_name.text = _loc.Get($"country_name.{selected.CountryId}");
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

			_influenceState = influence;
			RefreshInfluence(influence);
			_resourcesView.Refresh(resources);
			_charactersView.Refresh(characters);
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
					_charsSlide.pickingMode = PickingMode.Position;
				} else {
					_charsSlide.RemoveFromClassList("characters-slide--open");
					_charsSlide.pickingMode = PickingMode.Ignore;
				}
			}
			if (_charsToggleBtn != null) {
				var lbl = _charsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = open ? "Characters ▼" : "Characters ▲"; }
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
				if (lbl != null) { lbl.text = open ? "Actions ▼" : "Actions ▲"; }
			}
			OnSubPanelOpened?.Invoke(open);
		}

		public void RefreshUsedInfluence(int usedDisplay) {
			if (_influenceRow == null || _influenceLabel == null) { return; }
			int pool = _influenceState != null ? _influenceState.PoolSize : 100;
			_influenceLabel.text = $"{_loc.Get("hud.country_influence")}: {usedDisplay}/{pool}";
		}

		void RefreshInfluence(CountryInfluenceState influence) {
			if (_influenceRow == null) {
				return;
			}
			_influenceRow.style.display = DisplayStyle.Flex;
			int used = influence != null ? influence.UsedInfluence : 0;
			int pool = influence != null ? influence.PoolSize : 100;
			_influenceLabel.text = $"{_loc.Get("hud.country_influence")}: {used}/{pool}";
		}

		VisualElement BuildInfluenceTooltip(TooltipContext ctx) {
			var root = new VisualElement();

			var header = new Label(_loc.Get("hud.influence_tooltip_title"));
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var influence = _influenceState;
			if (influence == null) {
				return root;
			}

			foreach (var entry in influence.OrgEntries) {
				var row = new Label($"{entry.DisplayName}: {entry.Influence}");
				row.AddToClassList("tooltip-effect-name");
				row.AddToClassList("tooltip-effect-positive");
				row.AddToClassList("tooltip-inner-trigger");
				root.Add(row);

				var capturedEntry = entry;
				ctx.RegisterInnerTrigger(row, $"org-influence-{entry.OrgId}", _ =>
					BuildOrgInfluenceInnerTooltip(capturedEntry));
			}

			return root;
		}

		VisualElement BuildOrgInfluenceInnerTooltip(OrgInfluenceEntry entry) {
			var root = new VisualElement();

			var header = new Label(entry.DisplayName);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			var influenceRow = new Label($"{_loc.Get("hud.country_influence")}: {entry.Influence}");
			influenceRow.AddToClassList("tooltip-effect-name");
			root.Add(influenceRow);

			var baseRow = new Label($"  {_loc.Get("hud.influence_tooltip_base")} +{entry.BaseInfluence}");
			baseRow.AddToClassList("tooltip-effect-name");
			baseRow.AddToClassList("tooltip-effect-positive");
			root.Add(baseRow);

			if (entry.PermanentInfluence > 0) {
				var permRow = new Label($"  {_loc.Get("hud.influence_tooltip_permanent")} +{entry.PermanentInfluence}");
				permRow.AddToClassList("tooltip-effect-name");
				permRow.AddToClassList("tooltip-effect-positive");
				root.Add(permRow);
			}

			var leadsTo = new Label(_loc.Get("hud.influence_tooltip_leads_to"));
			leadsTo.AddToClassList("tooltip-effect-name");
			root.Add(leadsTo);

			var incomeRow = new Label($"  {_loc.Get("hud.influence_tooltip_income")} +{entry.EstimatedMonthlyGold:F1}/month");
			incomeRow.AddToClassList("tooltip-effect-name");
			incomeRow.AddToClassList("tooltip-effect-positive");
			root.Add(incomeRow);

			return root;
		}
	}
}
