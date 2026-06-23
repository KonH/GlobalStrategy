using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CountryActionsView {
		readonly VisualElement _handContainer;
		readonly ILocalization _loc;
		readonly ActionConfig _config;
		readonly ActionVisualConfig _visualConfig;
		readonly TooltipSystem _tooltip;

		public Action<string, string, VisualElement> OnCardClicked;
		public VisualElement DeckPileElement { get; private set; }
		public VisualElement HandContainer => _handContainer;
		public bool SuppressRefresh { get; set; }

		public CountryActionsView(
			VisualElement handContainer,
			ILocalization loc,
			ActionConfig config,
			ActionVisualConfig visualConfig,
			TooltipSystem tooltip) {
			_handContainer = handContainer;
			_loc = loc;
			_config = config;
			_visualConfig = visualConfig;
			_tooltip = tooltip;
		}

		public void Refresh(CountryActionsState state, CountryResourcesState orgResources) {
			if (SuppressRefresh) { return; }
			_handContainer.Clear();
			_handContainer.Add(BuildDeckPile(state.Deck.Count));
			foreach (var card in state.Hand) {
				_handContainer.Add(BuildHandCard(card, orgResources));
			}
		}

		VisualElement BuildHandCard(ActionCardEntry card, CountryResourcesState orgResources) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");

			var def = _config?.Find(card.ActionId);
			string name = def != null ? _loc.Get(def.NameKey) : card.ActionId;
			string descText = def != null ? _loc.Get(def.DescKey) : "";
			string goldCostText = GetGoldCostText(def);
			var sprite = _visualConfig?.FindFront(card.ActionId);

			double goldCost = GetGoldCost(def);
			bool canAffordGold = goldCost <= 0 || GetResourceValue(orgResources, "gold") >= goldCost;
			bool canPlay = !card.IsUnplayable && canAffordGold;

			var result = ActionCardBuilder.Build(name, descText, goldCostText, sprite);
			var cardEl = result.Card;
			cardEl.AddToClassList(canPlay ? "action-card--available" : "action-card--unavailable");

			if (result.CostLabel != null && !canAffordGold) {
				result.CostLabel.AddToClassList("action-card-cost-label--unaffordable");
			}

			if (card.IsUnplayable && !string.IsNullOrEmpty(card.UnplayableReason)) {
				int minInfluence = def != null ? ExtractMinInfluence(def) : 0;
				string reasonText = card.UnplayableReason == "pool_full"
					? _loc.Get("action.country.unplayable.pool_full")
					: string.Format(_loc.Get("action.country.unplayable.insufficient_influence"), minInfluence);
				var reasonLabel = new Label(reasonText);
				reasonLabel.AddToClassList("action-card-unplayable-reason");
				cardEl.Add(reasonLabel);
			}

			if (canPlay) {
				string capturedAction = card.ActionId;
				cardEl.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && cardEl.ContainsPoint(e.localPosition)) {
						OnCardClicked?.Invoke(capturedAction, null, cardEl);
					}
				});
			}

			wrapper.Add(cardEl);

			if (def != null) {
				var capDef = def;
				_tooltip.RegisterTrigger(wrapper, $"country-action-{card.ActionId}", _ => {
					var t = new VisualElement();
					var d = new Label(_loc.Get(capDef.DescKey));
					d.AddToClassList("gs-content");
					t.Add(d);
					return t;
				}, new HashSet<string>());
			}

			return wrapper;
		}

		VisualElement BuildDeckPile(int deckCount) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");
			wrapper.AddToClassList("action-deck-wrapper");
			wrapper.style.width = 240;

			var sprite = _visualConfig?.defaultBackImage;
			int shadowCount = Mathf.Min(Mathf.Max(deckCount - 1, 0), 3);

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
			wrapper.Add(front);

			return wrapper;
		}

		static int ExtractMinInfluence(ActionDefinition def) {
			foreach (var cond in def.Conditions) {
				if (cond.Type == "gte" && cond.Members != null && cond.Members.Count >= 2) {
					if (cond.Members[0].Type == "influence" && cond.Members[1].Type == "value") {
						return (int)cond.Members[1].Value;
					}
				}
			}
			return 0;
		}

		static double GetGoldCost(ActionDefinition def) {
			if (def == null) { return 0; }
			foreach (var c in def.Cost) {
				if (c.ResourceId == "gold") { return c.Amount; }
			}
			return 0;
		}

		static string GetGoldCostText(ActionDefinition def) {
			double gold = GetGoldCost(def);
			if (gold == 0) { return null; }
			return gold == System.Math.Floor(gold) ? $"{(int)gold}" : $"{gold:F1}";
		}

		static double GetResourceValue(CountryResourcesState resources, string resourceId) {
			if (resources == null) { return 0; }
			foreach (var r in resources.Resources) {
				if (r.ResourceId == resourceId) { return r.Value.Display; }
			}
			return 0;
		}
	}
}
