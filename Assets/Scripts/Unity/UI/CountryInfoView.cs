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
	public class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly VisualElement? _flagElement;
		readonly Label _controlLabel;
		readonly VisualElement? _controlRow;
		readonly VisualElement? _charsSlide;
		readonly Button? _charsToggleBtn;
		readonly VisualElement? _actionsSlide;
		readonly Button? _actionsToggleBtn;
		readonly VisualElement? _friendsRowBlock;
		readonly Label? _friendsHeader;
		readonly VisualElement? _friendsFlags;
		readonly VisualElement? _rivalsRowBlock;
		readonly Label? _rivalsHeader;
		readonly VisualElement? _rivalsFlags;
		readonly VisualElement? _warsRowBlock;
		readonly Label? _warsHeader;
		readonly VisualElement? _warsFlags;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CharactersView _charactersView;
		readonly CountryVisualConfig? _countryVisualConfig;
		readonly OrgVisualConfig? _orgVisualConfig;
		readonly TooltipSystem _tooltip;
		readonly GameSettings? _gameSettings;
		CountryActionsView? _actionsView;
		CountryControlState? _controlState;
		bool _charsOpen;
		bool _actionsOpen;
		string? _lastCountryId;
		readonly CountryActionsVisibility _actionsVisibility;

		public event Action<bool>? OnSubPanelOpened;
		public event Action<string, string, int, VisualElement, ActionCardBuilder.CountryCardFace>? OnCountryActionCardClicked;
		public event Action<string, string, int, VisualElement, ActionCardBuilder.CountryCardFace>? OnCountryActionCardDiscarded;
		public event Action<ActionConditionDebugEntry>? OnUnplayableCountryActionCardReleased;
		public event Action? OnCountryActionCardDiscardUnaffordable;
		public event Action<string>? OnRelatedCountryFlagClicked;
		public CountryActionsView? ActionsView => _actionsView;
		public CharactersView CharactersView => _charactersView;
		public bool IsCharactersOpen => _charsOpen;
		public bool IsActionsOpen => _actionsOpen;
		public void OpenChars() => SetCharsOpen(true);
		public void EnsureActionsOpen() {
			if (!_actionsOpen) {
				SetActionsOpen(true);
			}
		}

		public CountryInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig characterVisualConfig, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CountryVisualConfig? countryVisualConfig = null, OrgVisualConfig? orgVisualConfig = null, GameSettings? gameSettings = null, CountryActionsVisibility? actionsVisibility = null) {
			_root = root;
			_gameSettings = gameSettings;
			// Defaults ActionsPanelOpen to false to match _actionsOpen's own default - keeps
			// VisualStateConverter from building full per-card hand detail before the first
			// real SetActionsOpen call (country selection / toggle click) can sync them.
			_actionsVisibility = actionsVisibility ?? new CountryActionsVisibility();
			_actionsVisibility.ActionsPanelOpen = false;
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
			_friendsRowBlock = root.Q("friends-row-block");
			_friendsHeader = root.Q<Label>("friends-header");
			_friendsFlags = root.Q("friends-flags");
			_rivalsRowBlock = root.Q("rivals-row-block");
			_rivalsHeader = root.Q<Label>("rivals-header");
			_rivalsFlags = root.Q("rivals-flags");
			_warsRowBlock = root.Q("wars-row-block");
			_warsHeader = root.Q<Label>("wars-header");
			_warsFlags = root.Q("wars-flags");
			_loc = loc;
			_tooltip = tooltip;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_charactersView = new CharactersView(root.Q("characters-container"), loc, characterConfig, tooltip, characterVisualConfig, actionConfig, actionVisualConfig);

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
						loc,
						actionConfig,
						actionVisualConfig,
						countryVisualConfig,
						tooltip,
						gameSettings?.DiscardGoldCost ?? 50);
					_actionsView.OnCardClicked = (actionId, targetCountryId, slotIndex, element, face) =>
						OnCountryActionCardClicked?.Invoke(actionId, targetCountryId, slotIndex, element, face);
					_actionsView.OnCardDiscarded = (actionId, targetCountryId, slotIndex, element, faceData) =>
						OnCountryActionCardDiscarded?.Invoke(actionId, targetCountryId, slotIndex, element, faceData);
					_actionsView.OnUnplayableCardReleased = condition =>
						OnUnplayableCountryActionCardReleased?.Invoke(condition);
					_actionsView.OnDiscardUnaffordable = () =>
						OnCountryActionCardDiscardUnaffordable?.Invoke();
				}
			}
			if (_charsToggleBtn != null) {
				_charsToggleBtn.OnClick(ToggleChars);
			}
			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.OnClick(ToggleActions);
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
				if (_friendsHeader != null) { _friendsHeader.text = _loc.Get("hud.friends"); }
				if (_rivalsHeader != null) { _rivalsHeader.text = _loc.Get("hud.rivals"); }
				if (_warsHeader != null) { _warsHeader.text = _loc.Get("hud.wars"); }
				bool enableFriendsRelation = _gameSettings?.FeatureFlags?.EnableFriendsRelation ?? true;
				if (enableFriendsRelation) {
					BuildRelationsRow(_friendsFlags, _friendsRowBlock, selected.Relations.Friends, "relation");
				} else if (_friendsRowBlock != null) {
					_friendsRowBlock.style.display = DisplayStyle.None;
				}
				BuildRelationsRow(_rivalsFlags, _rivalsRowBlock, selected.Relations.Rivals, "relation");
				BuildRelationsRow(_warsFlags, _warsRowBlock, selected.Wars.Opponents, "war");
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

			bool hasActions = countryActions != null
				&& (countryActions.Hand.Count > 0
					|| countryActions.Deck.Count > 0
					|| countryActions.HasPendingDraw
					|| countryActions.DrawChoices.Count > 0);
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
			if (!_actionsOpen && _actionsSlide != null) { SetPickingModeRecursive(_actionsSlide, PickingMode.Ignore); }
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
			_actionsVisibility.ActionsPanelOpen = open;
			if (_actionsSlide != null) {
				if (open) {
					_actionsSlide.AddToClassList("actions-slide--open");
					SetPickingModeRecursive(_actionsSlide, PickingMode.Position);
				} else {
					_actionsSlide.RemoveFromClassList("actions-slide--open");
					SetPickingModeRecursive(_actionsSlide, PickingMode.Ignore);
					_tooltip?.HideAll();
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
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, _loc.Get("hud.control_tooltip_title"));

			var control = _controlState;
			if (control == null) {
				return root;
			}

			foreach (var entry in control.OrgEntries) {
				FlagNameHeaderBuilder.Elements row = FlagNameHeaderBuilder.Build("entity-flag");
				row.Row.AddToClassList("tooltip-inner-trigger");
				row.Label.AddToClassList("tooltip-effect-name");
				row.Label.AddToClassList("tooltip-effect-positive");
				var orgSprite = _orgVisualConfig?.Find(entry.OrgId)?.flag;
				FlagNameHeaderBuilder.Bind(row, orgSprite, $"{entry.DisplayName}: {entry.Control}");
				root.Add(row.Row);

				var capturedEntry = entry;
				ctx.RegisterInnerTrigger(row.Row, $"org-control-{entry.OrgId}", _ =>
					BuildOrgControlInnerTooltip(capturedEntry));
			}

			return root;
		}

		VisualElement BuildOrgControlInnerTooltip(OrgControlEntry entry) {
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, entry.DisplayName);

			TooltipBodyBuilder.AddLine(root, $"{_loc.Get("hud.country_control")}: {entry.Control}");
			TooltipBodyBuilder.AddLine(root, $"  {_loc.Get("hud.control_tooltip_base")} +{entry.BaseControl}", TooltipBodyBuilder.LineTone.Positive);

			if (entry.PermanentControl > 0) {
				TooltipBodyBuilder.AddLine(root, $"  {_loc.Get("hud.control_tooltip_permanent")} +{entry.PermanentControl}", TooltipBodyBuilder.LineTone.Positive);
			}

			TooltipBodyBuilder.AddLine(root, _loc.Get("hud.control_tooltip_leads_to"));
			TooltipBodyBuilder.AddLine(root, $"  {_loc.Get("hud.control_tooltip_income")} +{entry.EstimatedMonthlyGold:F1}/month", TooltipBodyBuilder.LineTone.Positive);

			return root;
		}

		void BuildRelationsRow(VisualElement? container, VisualElement? rowBlock, IReadOnlyList<string> countryIds, string keyPrefix) {
			if (container == null || rowBlock == null) {
				return;
			}
			container.Clear();
			rowBlock.style.display = countryIds.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
			for (int i = 0; i < countryIds.Count; i++) {
				var countryId = countryIds[i];
				var flagEl = FlagBadgeBuilder.Build("relations-flag");
				if (i > 0) {
					flagEl.style.marginLeft = 8;
				}
				var sprite = _countryVisualConfig?.Find(countryId)?.flag;
				FlagBadgeBuilder.Bind(flagEl, sprite);
				flagEl.OnClick(() => OnRelatedCountryFlagClicked?.Invoke(countryId));
				_tooltip.RegisterTrigger(flagEl, $"{keyPrefix}-{countryId}-{i}", _ => BuildRelationTooltip(countryId), new HashSet<string>());
				container.Add(flagEl);
			}
		}

		VisualElement BuildRelationTooltip(string countryId) {
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, _loc.Get($"country_name.{countryId}"));
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
