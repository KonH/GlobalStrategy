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

		public Action<string> OnCardClicked;

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
			var nameLabel = new Label(name);
			nameLabel.AddToClassList("action-card-name");
			cardEl.Add(nameLabel);

			var img = new VisualElement();
			img.AddToClassList("action-card-image");
			var sprite = _visualConfig?.FindFront(card.ActionId);
			if (sprite != null) {
				img.style.backgroundImage = new StyleBackground(sprite);
			}
			cardEl.Add(img);

			if (def != null && def.Prices.Count > 0) {
				foreach (var price in def.Prices) {
					var resConfig = _resourceConfig?.FindResource(price.ResourceId);
					string resName = resConfig != null ? _loc.Get(resConfig.NameKey) : price.ResourceId;
					var priceLabel = new Label($"{price.Amount:F0} {resName}");
					priceLabel.AddToClassList("action-card-price");
					priceLabel.AddToClassList(canAfford ? "action-card-price--affordable" : "action-card-price--unaffordable");
					cardEl.Add(priceLabel);
				}
			}

			var playBtn = new Button();
			playBtn.AddToClassList("gs-btn");
			playBtn.AddToClassList("gs-btn--small");
			playBtn.AddToClassList("action-card-play-btn");
			playBtn.text = "Play";
			string capturedId = card.ActionId;
			if (canAfford) {
				playBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && playBtn.ContainsPoint(e.localPosition)) {
						OnCardClicked?.Invoke(capturedId);
					}
				});
			} else {
				playBtn.SetEnabled(false);
			}
			cardEl.Add(playBtn);

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
