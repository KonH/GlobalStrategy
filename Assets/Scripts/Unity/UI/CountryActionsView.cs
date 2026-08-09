using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class CountryActionsView {
		const long DiscardHoldMilliseconds = 1000;

		readonly VisualElement _handContainer;
		readonly ILocalization _loc;
		readonly ActionConfig _config;
		readonly ActionVisualConfig _visualConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly TooltipSystem _tooltip;
		readonly double _discardGoldCost;
		readonly Dictionary<int, VisualElement> _renderedCardsBySlot = new();
		ActiveGesture _activeGesture;
		Button _drawButton;
		Label _handSizeLabel;
		bool _presentationBusy;
		bool _canStartDraw;

		public Action<string, string, int, VisualElement, ActionCardBuilder.CountryCardFace> OnCardClicked;
		public Action<string, string, int, VisualElement, ActionCardBuilder.CountryCardFace> OnCardDiscarded;
		public Action OnDrawRequested;
		public Action<ActionConditionDebugEntry> OnUnplayableCardReleased;
		public Action OnDiscardUnaffordable;
		public VisualElement DeckPileElement { get; private set; }
		public VisualElement HandContainer => _handContainer;
		public bool SuppressRefresh { get; set; }

		public CountryActionsView(
			VisualElement handContainer,
			ILocalization loc,
			ActionConfig config,
			ActionVisualConfig visualConfig,
			CountryVisualConfig countryVisualConfig,
			TooltipSystem tooltip,
			double discardGoldCost) {
			_handContainer = handContainer;
			_loc = loc;
			_config = config;
			_visualConfig = visualConfig;
			_countryVisualConfig = countryVisualConfig;
			_tooltip = tooltip;
			_discardGoldCost = Math.Max(0, discardGoldCost);
		}

		public void Refresh(CountryActionsState state, CountryResourcesState playerResources) {
			RefreshDeckControls(state);
			if (SuppressRefresh) {
				return;
			}
			// Unpaused ticks rebuild the hand every frame; tearing down the held card
			// cancels the 1s discard hold before it can complete.
			if (_activeGesture != null) {
				return;
			}
			_renderedCardsBySlot.Clear();
			_handContainer.Clear();
			_handContainer.Add(BuildDeckPile(state));
			foreach (var card in state.Hand) {
				_handContainer.Add(BuildHandCard(card, playerResources));
			}
		}

		public void SetPresentationBusy(bool busy) {
			_presentationBusy = busy;
			if (busy) {
				CancelActiveGesture();
			}
			RefreshDrawAvailability();
		}

		internal bool TryGetFaceData(ActionCardEntry card, out ActionCardBuilder.CountryCardFace faceData) {
			if (card == null) {
				faceData = null;
				return false;
			}
			faceData = ComposeFaceData(card);
			return true;
		}

		internal bool TryGetRenderedCard(int slotIndex, out VisualElement card) =>
			_renderedCardsBySlot.TryGetValue(slotIndex, out card);

		VisualElement BuildHandCard(ActionCardEntry card, CountryResourcesState playerResources) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");

			ActionCardBuilder.CountryCardFace face = ComposeFaceData(card);
			var result = ActionCardBuilder.Build(face, includeDiscardHint: true);
			VisualElement cardEl = result.Card;
			cardEl.AddToClassList(card.CanPlay ? "action-card--available" : "action-card--unavailable");
			_renderedCardsBySlot[card.SlotIndex] = cardEl;

			bool canAffordCardGold = FindGoldRequirement(card.Conditions)?.Passed ?? true;
			if (result.CostLabel != null && !canAffordCardGold) {
				result.CostLabel.AddToClassList("action-card-cost-label--unaffordable");
			}

			bool canAffordDiscard = GetResourceValue(playerResources, "gold") >= _discardGoldCost;
			if (result.DiscardHint != null) {
				result.DiscardHint.style.display = DisplayStyle.None;
				result.DiscardHint.pickingMode = PickingMode.Ignore;
			}
			if (result.DiscardHintLabel != null) {
				result.DiscardHintLabel.text = _loc.Get("action.discard.hint");
			}
			if (result.DiscardHintPrice != null) {
				result.DiscardHintPrice.text = FormatNumber(_discardGoldCost);
				result.DiscardHintPrice.EnableInClassList("action-card-cost-label--unaffordable", !canAffordDiscard);
			}

			RegisterGesture(cardEl, card, face, result.DiscardHint, canAffordDiscard);
			wrapper.Add(cardEl);

			if (result.PlayableCountriesBadge != null) {
				ActionCardBuilder.CountryCardFace capturedFace = face;
				_tooltip.RegisterTrigger(
					result.PlayableCountriesBadge,
					$"country-action-playable-{card.ActionId}-{card.SlotIndex}",
					_ => BuildPlayableCountriesTooltip(capturedFace.PlayableCountries),
					new HashSet<string>());
			}

			var def = _config?.Find(card.ActionId);
			if (def != null) {
				var capturedDefinition = def;
				var descriptionTrigger = cardEl.Q<Label>(className: "action-card-desc");
				_tooltip.RegisterTrigger(descriptionTrigger, $"country-action-{card.ActionId}-{card.SlotIndex}", _ => {
					var tooltip = new VisualElement();
					var description = new Label(_loc.Get(capturedDefinition.DescKey));
					description.AddToClassList("gs-content");
					tooltip.Add(description);
					return tooltip;
				}, new HashSet<string>());
			}

			return wrapper;
		}

		ActionCardBuilder.CountryCardFace ComposeFaceData(ActionCardEntry card) {
			var definition = _config?.Find(card.ActionId);
			string name;
			if (definition == null) {
				name = card.ActionId;
			} else if (!string.IsNullOrEmpty(card.TargetCountryId)) {
				name = string.Format(_loc.Get(definition.NameKey), _loc.Get($"country_name.{card.TargetCountryId}"));
			} else {
				name = _loc.Get(definition.NameKey);
			}

			var requirements = new List<ActionCardBuilder.RequirementRow>();
			foreach (ActionConditionDebugEntry condition in card.Conditions) {
				if (condition.Passed) {
					continue;
				}
				requirements.Add(new ActionCardBuilder.RequirementRow(
					ActionConditionText.Localize(_loc, condition),
					condition.Passed));
			}

			var playableCountries = new List<ActionCardBuilder.PlayableCountryBadgeItem>(card.PlayableCountryIds.Count);
			foreach (string countryId in card.PlayableCountryIds) {
				Sprite flag = _countryVisualConfig?.Find(countryId)?.flag;
				playableCountries.Add(new ActionCardBuilder.PlayableCountryBadgeItem(countryId, flag));
			}

			return new ActionCardBuilder.CountryCardFace(
				name,
				definition != null ? _loc.Get(definition.DescKey) : "",
				GetGoldCostText(definition),
				_visualConfig?.FindFront(card.ActionId),
				card.WarWinChancePercent,
				card.CooldownFractionRemaining,
				card.CooldownRemainingDays,
				requirements,
				playableCountries);
		}

		void RegisterGesture(
			VisualElement cardElement,
			ActionCardEntry card,
			ActionCardBuilder.CountryCardFace face,
			VisualElement discardHint,
			bool canAffordDiscard) {
			cardElement.RegisterCallback<PointerDownEvent>(e => {
				if (e.button != 0 || _presentationBusy) {
					return;
				}
				CancelActiveGesture();
				var gesture = new ActiveGesture {
					CardElement = cardElement,
					Card = card,
					Face = face,
					DiscardHint = discardHint,
					CanAffordDiscard = canAffordDiscard,
					PointerId = e.pointerId,
					PointerOverCard = true
				};
				_activeGesture = gesture;
				cardElement.CapturePointer(e.pointerId);
				gesture.HoldSchedule = cardElement.schedule.Execute(() => ShowDiscardHint(gesture))
					.StartingIn(DiscardHoldMilliseconds);
				e.StopPropagation();
			});

			cardElement.RegisterCallback<PointerMoveEvent>(e => {
				if (IsActiveGesture(cardElement, e.pointerId)) {
					_activeGesture.PointerOverCard = cardElement.ContainsPoint(e.localPosition);
				}
			});
			cardElement.RegisterCallback<PointerEnterEvent>(e => {
				if (IsActiveGesture(cardElement, e.pointerId)) {
					_activeGesture.PointerOverCard = true;
				}
			});
			cardElement.RegisterCallback<PointerLeaveEvent>(e => {
				if (IsActiveGesture(cardElement, e.pointerId)) {
					_activeGesture.PointerOverCard = false;
				}
			});

			cardElement.RegisterCallback<PointerUpEvent>(e => {
				if (e.button != 0 || !IsActiveGesture(cardElement, e.pointerId)) {
					return;
				}
				ActiveGesture gesture = _activeGesture;
				bool releasedOnCard = gesture.PointerOverCard && cardElement.ContainsPoint(e.localPosition);
				bool discardRequested = gesture.HintVisible;
				CancelActiveGesture();
				e.StopPropagation();

				if (!releasedOnCard) {
					return;
				}
				if (discardRequested) {
					if (gesture.CanAffordDiscard) {
						OnCardDiscarded?.Invoke(
							gesture.Card.ActionId,
							gesture.Card.TargetCountryId,
							gesture.Card.SlotIndex,
							gesture.CardElement,
							gesture.Face);
					} else {
						OnDiscardUnaffordable?.Invoke();
					}
					return;
				}

				if (gesture.Card.CanPlay) {
					OnCardClicked?.Invoke(
						gesture.Card.ActionId,
						gesture.Card.TargetCountryId,
						gesture.Card.SlotIndex,
						gesture.CardElement,
						gesture.Face);
				} else if (gesture.Card.FirstFailure != null) {
					OnUnplayableCardReleased?.Invoke(gesture.Card.FirstFailure);
				} else {
					OnUnplayableCardReleased?.Invoke(new ActionConditionDebugEntry(
						"card unavailable",
						false,
						"action.requirement.unavailable",
						Array.Empty<string>(),
						"unavailable"));
				}
			});

			cardElement.RegisterCallback<PointerCancelEvent>(e => {
				if (!IsActiveGesture(cardElement, e.pointerId)) {
					return;
				}
				CancelActiveGesture();
				e.StopPropagation();
			});
			cardElement.RegisterCallback<PointerCaptureOutEvent>(e => {
				if (IsActiveGesture(cardElement, e.pointerId)) {
					CancelActiveGesture(releasePointer: false);
				}
			});
			cardElement.RegisterCallback<DetachFromPanelEvent>(_ => {
				if (_activeGesture?.CardElement == cardElement) {
					CancelActiveGesture();
				}
			});
		}

		void ShowDiscardHint(ActiveGesture gesture) {
			if (_activeGesture != gesture || gesture.CardElement.panel == null) {
				return;
			}
			gesture.HoldSchedule = null;
			gesture.HintVisible = true;
			if (gesture.DiscardHint != null) {
				gesture.DiscardHint.style.display = DisplayStyle.Flex;
			}
		}

		bool IsActiveGesture(VisualElement cardElement, int pointerId) =>
			_activeGesture != null
			&& _activeGesture.CardElement == cardElement
			&& _activeGesture.PointerId == pointerId;

		void CancelActiveGesture(bool releasePointer = true) {
			ActiveGesture gesture = _activeGesture;
			if (gesture == null) {
				return;
			}
			_activeGesture = null;
			gesture.HoldSchedule?.Pause();
			gesture.HoldSchedule = null;
			gesture.HintVisible = false;
			if (gesture.DiscardHint != null) {
				gesture.DiscardHint.style.display = DisplayStyle.None;
			}
			if (releasePointer && gesture.CardElement.HasPointerCapture(gesture.PointerId)) {
				gesture.CardElement.ReleasePointer(gesture.PointerId);
			}
		}

		VisualElement BuildPlayableCountriesTooltip(
			IReadOnlyList<ActionCardBuilder.PlayableCountryBadgeItem> countries) {
			var root = new VisualElement();
			foreach (ActionCardBuilder.PlayableCountryBadgeItem country in countries) {
				var row = new VisualElement();
				row.AddToClassList("flag-name-row");

				var flag = new VisualElement();
				flag.AddToClassList("relations-flag");
				flag.pickingMode = PickingMode.Ignore;
				if (country.Flag != null) {
					flag.style.backgroundImage = new StyleBackground(country.Flag);
				} else {
					flag.style.display = DisplayStyle.None;
				}
				row.Add(flag);

				var label = new Label(_loc.Get($"country_name.{country.CountryId}"));
				label.AddToClassList("gs-content");
				row.Add(label);
				root.Add(row);
			}
			return root;
		}

		VisualElement BuildDeckPile(CountryActionsState state) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");
			wrapper.AddToClassList("action-deck-wrapper");
			wrapper.style.width = 240;

			var sprite = _visualConfig?.defaultBackImage;
			int shadowCount = Mathf.Min(Mathf.Max(state.Deck.Count - 1, 0), 3);
			for (int i = shadowCount; i >= 1; i--) {
				int offset = i * 5;
				var shadow = new VisualElement();
				shadow.AddToClassList("action-card");
				shadow.AddToClassList("action-card--available");
				shadow.style.position = Position.Absolute;
				shadow.style.left = offset;
				shadow.style.top = offset;
				if (sprite != null) {
					shadow.style.backgroundImage = new StyleBackground(sprite);
					shadow.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
				}
				wrapper.Add(shadow);
			}

			var front = new VisualElement();
			front.AddToClassList("action-card");
			front.AddToClassList("action-card--available");
			if (sprite != null) {
				front.style.backgroundImage = new StyleBackground(sprite);
				front.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
			}
			DeckPileElement = front;

			var controls = new VisualElement();
			controls.AddToClassList("action-deck-controls");
			_drawButton = new Button();
			_drawButton.text = _loc.Get("action.draw.button");
			_drawButton.AddToClassList("gs-btn");
			_drawButton.AddToClassList("gs-btn--small");
			_drawButton.AddToClassList("action-deck-draw-button");
			_drawButton.RegisterCallback<PointerUpEvent>(e => {
				if (e.button != 0
					|| !_drawButton.enabledSelf
					|| !_drawButton.ContainsPoint(e.localPosition)) {
					return;
				}
				OnDrawRequested?.Invoke();
				e.StopPropagation();
			});
			controls.Add(_drawButton);

			_handSizeLabel = new Label();
			_handSizeLabel.AddToClassList("gs-label");
			_handSizeLabel.AddToClassList("action-deck-hand-size");
			controls.Add(_handSizeLabel);
			RefreshDeckControls(state);
			front.Add(controls);
			wrapper.Add(front);
			return wrapper;
		}

		void RefreshDeckControls(CountryActionsState state) {
			if (state == null) {
				return;
			}
			_canStartDraw = state.CanStartDraw;
			if (_drawButton != null) {
				_drawButton.text = _loc.Get("action.draw.button");
			}
			if (_handSizeLabel == null) {
				RefreshDrawAvailability();
				return;
			}
			try {
				_handSizeLabel.text = string.Format(
					_loc.Get("action.draw.hand_size"),
					state.Hand.Count,
					state.HandSize);
			} catch (FormatException exception) {
				Debug.LogError($"[CountryActionsView] Invalid hand-size locale format: {exception.Message}");
				_handSizeLabel.text = $"{state.Hand.Count}/{state.HandSize}";
			}
			RefreshDrawAvailability();
		}

		void RefreshDrawAvailability() {
			_drawButton?.SetEnabled(_canStartDraw && !_presentationBusy);
		}

		static ActionConditionDebugEntry FindGoldRequirement(IReadOnlyList<ActionConditionDebugEntry> conditions) {
			foreach (ActionConditionDebugEntry condition in conditions) {
				if (condition.LocaleKey == "action.requirement.gold") {
					return condition;
				}
			}
			return null;
		}

		static double GetGoldCost(ActionDefinition definition) {
			if (definition == null) {
				return 0;
			}
			foreach (var cost in definition.Cost) {
				if (cost.ResourceId == "gold") {
					return cost.Amount;
				}
			}
			return 0;
		}

		static string GetGoldCostText(ActionDefinition definition) {
			double gold = GetGoldCost(definition);
			return gold == 0 ? null : FormatNumber(gold);
		}

		static string FormatNumber(double value) =>
			value.ToString("0.##", CultureInfo.InvariantCulture);

		static double GetResourceValue(CountryResourcesState resources, string resourceId) {
			if (resources == null) {
				return 0;
			}
			foreach (var resource in resources.Resources) {
				if (resource.ResourceId == resourceId) {
					return resource.Value.Display;
				}
			}
			return 0;
		}

		sealed class ActiveGesture {
			public VisualElement CardElement;
			public ActionCardEntry Card;
			public ActionCardBuilder.CountryCardFace Face;
			public VisualElement DiscardHint;
			public IVisualElementScheduledItem HoldSchedule;
			public int PointerId;
			public bool PointerOverCard;
			public bool HintVisible;
			public bool CanAffordDiscard;
		}
	}

	static class ActionConditionText {
		public static string Localize(ILocalization localization, ActionConditionDebugEntry condition) {
			if (condition == null) {
				return "";
			}
			string format = localization.Get(condition.LocaleKey);
			try {
				return string.Format(format, ToLocalizedFormatArguments(localization, condition));
			} catch (FormatException exception) {
				Debug.LogError(
					$"[ActionConditionText] Invalid locale format for '{condition.LocaleKey}': {exception.Message}");
				return format;
			}
		}

		public static object[] ToLocalizedFormatArguments(
			ILocalization localization,
			ActionConditionDebugEntry condition) {
			IReadOnlyList<string> arguments = condition.LocaleArguments;
			if (arguments == null || arguments.Count == 0) {
				return Array.Empty<object>();
			}
			var result = new object[arguments.Count];
			for (int i = 0; i < arguments.Count; i++) {
				result[i] = arguments[i];
			}
			if (condition.LocaleKey == "action.requirement.primary_country" && arguments.Count > 0) {
				result[0] = localization.Get($"country_name.{arguments[0]}");
			}
			if (condition.LocaleKey == "action.country.unplayable.country_no_longer_exists" && arguments.Count > 0) {
				result[0] = localization.Get($"country_name.{arguments[0]}");
			}
			if (condition.LocaleKey == "action.requirement.resource" && arguments.Count > 2) {
				result[2] = localization.Get($"resource.{arguments[2]}.name");
			}
			if (condition.LocaleKey == "action.requirement.opinion_min_role" && arguments.Count > 0) {
				result[0] = localization.Get($"character.role.{arguments[0]}.name");
			}
			return result;
		}
	}
}
