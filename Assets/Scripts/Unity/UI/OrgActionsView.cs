using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class OrgActionsView {
		readonly VisualElement _handContainer;
		readonly ILocalization _loc;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _visualConfig;
		readonly ResourceConfig _resourceConfig;
		readonly TooltipSystem _tooltip;

		public Action<string, VisualElement> OnCardClicked;

		public VisualElement DeckPileElement { get; private set; }
		public VisualElement HandContainer => _handContainer;

		public bool SuppressRefresh { get; set; }

		public OrgActionsView(
			VisualElement handContainer,
			ILocalization loc,
			ActionConfig actionConfig,
			ActionVisualConfig visualConfig,
			ResourceConfig resourceConfig,
			TooltipSystem tooltip) {
			_handContainer = handContainer;
			_loc = loc;
			_actionConfig = actionConfig;
			_visualConfig = visualConfig;
			_resourceConfig = resourceConfig;
			_tooltip = tooltip;
		}

		public void Refresh(OrgActionsState state, CountryResourcesState resources) {
			if (SuppressRefresh) { return; }
			_handContainer.Clear();

			_handContainer.Add(BuildDeckPile(state.Deck.Count));

			foreach (var card in state.Hand) {
				var actionDef = _actionConfig?.Find(card.ActionId);
				bool canAfford = actionDef != null && CanAffordAll(actionDef, resources);
				_handContainer.Add(BuildHandCard(card, actionDef, canAfford));
			}
		}

		VisualElement BuildHandCard(ActionCardEntry card, ActionDefinition def, bool canAfford) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");

			var cardEl = new VisualElement();
			cardEl.AddToClassList("action-card");
			cardEl.AddToClassList(canAfford ? "action-card--available" : "action-card--unavailable");

			string name = def != null ? _loc.Get(def.NameKey) : card.ActionId;
			var header = new Label(name);
			header.AddToClassList("action-card-header");
			cardEl.Add(header);

			var art = new VisualElement();
			art.AddToClassList("action-card-art");
			var sprite = _visualConfig?.FindFront(card.ActionId);
			if (sprite != null) {
				art.style.backgroundImage = new StyleBackground(sprite);
			}
			cardEl.Add(art);

			var body = new VisualElement();
			body.AddToClassList("action-card-body");
			if (def != null) {
				var desc = new Label(_loc.Get(def.DescKey));
				desc.AddToClassList("action-card-desc");
				body.Add(desc);

				var footer = new VisualElement();
				footer.AddToClassList("action-card-footer");

				var pct = new Label($"{(int)(def.SuccessRate * 100)}%");
				pct.AddToClassList("action-card-success-pct");
				footer.Add(pct);

				if (def.Prices.Count > 0) {
					var costRow = new VisualElement();
					costRow.AddToClassList("action-card-cost");
					foreach (var price in def.Prices) {
						string amtStr = price.Amount == System.Math.Floor(price.Amount) ? $"{(int)price.Amount}" : $"{price.Amount:F1}";
						var costLabel = new Label(amtStr);
						costLabel.AddToClassList("action-card-cost-label");
						if (!canAfford) { costLabel.AddToClassList("action-card-cost-label--unaffordable"); }
						costRow.Add(costLabel);
					}
					var costIcon = new VisualElement();
					costIcon.AddToClassList("action-card-cost-icon");
					costRow.Add(costIcon);
					footer.Add(costRow);
				}

				body.Add(footer);
			}
			cardEl.Add(body);

			string capturedId = card.ActionId;
			if (canAfford) {
				cardEl.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && cardEl.ContainsPoint(e.localPosition)) {
						OnCardClicked?.Invoke(capturedId, cardEl);
					}
				});
			}

			wrapper.Add(cardEl);

			if (def != null) {
				RegisterTooltip(wrapper, def, canAfford, card.ActionId);
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

		void RegisterTooltip(VisualElement trigger, ActionDefinition def, bool available, string actionId) {
			bool cap = available;
			ActionDefinition capDef = def;
			_tooltip.RegisterTrigger(trigger, $"action-{actionId}", _ => BuildCardTooltip(capDef, cap), new HashSet<string>());
		}

		VisualElement BuildCardTooltip(ActionDefinition def, bool available) {
			var root = new VisualElement();
			string desc = _loc.Get(def.DescKey);
			int pct = (int)(def.SuccessRate * 100f);
			var descLabel = new Label($"{desc}\n{pct}% success");
			descLabel.AddToClassList("gs-content");
			root.Add(descLabel);
			string hintKey = available ? "action.tooltip.play_hint" : "action.tooltip.unaffordable_hint";
			var hint = new Label(_loc.Get(hintKey));
			hint.AddToClassList(available ? "gs-color-positive" : "gs-color-negative");
			root.Add(hint);
			return root;
		}

		static bool CanAffordAll(ActionDefinition def, CountryResourcesState resources) {
			foreach (var price in def.Prices) {
				if (GetResourceValue(resources, price.ResourceId) < price.Amount) { return false; }
			}
			return true;
		}

		static double GetResourceValue(CountryResourcesState resources, string resourceId) {
			if (resources == null) { return 0; }
			foreach (var r in resources.Resources) {
				if (r.ResourceId == resourceId) { return r.Value; }
			}
			return 0;
		}
	}
}
