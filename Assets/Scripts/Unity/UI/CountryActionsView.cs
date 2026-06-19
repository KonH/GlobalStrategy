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
		readonly CountryActionConfig _config;
		readonly ActionVisualConfig _visualConfig;
		readonly TooltipSystem _tooltip;

		public Action<string, string, VisualElement> OnCardClicked;
		public VisualElement DeckPileElement { get; private set; }
		public VisualElement HandContainer => _handContainer;
		public bool SuppressRefresh { get; set; }

		public CountryActionsView(
			VisualElement handContainer,
			ILocalization loc,
			CountryActionConfig config,
			ActionVisualConfig visualConfig,
			TooltipSystem tooltip) {
			_handContainer = handContainer;
			_loc = loc;
			_config = config;
			_visualConfig = visualConfig;
			_tooltip = tooltip;
		}

		public void Refresh(CountryActionsState state, CountryResourcesState resources) {
			if (SuppressRefresh) { return; }
			_handContainer.Clear();
			_handContainer.Add(BuildDeckPile(state.Deck.Count));
			foreach (var card in state.Hand) {
				_handContainer.Add(BuildHandCard(card, state.CurrentTime, resources));
			}
		}

		VisualElement BuildHandCard(CountryActionCardEntry card, DateTime currentTime, CountryResourcesState resources) {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");

			var def = _config?.Find(card.ActionId);
			string name = def != null ? _loc.Get(def.NameKey) : card.ActionId;
			string descText = def != null ? _loc.Get(def.DescKey) : "";
			string successPct = $"{(int)(card.SuccessRate * 100)}%";
			double gold = def?.GoldCost ?? 0;
			string goldCostText = gold == System.Math.Floor(gold) ? $"{(int)gold}" : $"{gold:F1}";
			var sprite = _visualConfig?.FindFront(card.ActionId);

			bool canPlay = !card.IsUnplayable && !card.IsOnCooldown;

			var result = ActionCardBuilder.Build(name, descText, successPct, goldCostText, sprite);
			var cardEl = result.Card;
			cardEl.AddToClassList(canPlay ? "action-card--available" : "action-card--unavailable");

			if (result.CostLabel != null && def != null) {
				bool canAfford = GetResourceValue(resources, "gold") >= def.GoldCost;
				if (!canAfford) { result.CostLabel.AddToClassList("action-card-cost-label--unaffordable"); }
			}

			if (card.IsRateDynamic && result.SuccessPct != null) {
				int capturedBase = card.InfluenceBase;
				int capturedBonus = card.InfluenceBonus;
				int capturedInfluence = card.CurrentOrgInfluence;
				_tooltip.RegisterTrigger(result.SuccessPct, $"country-action-rate-{card.ActionId}-{card.TargetCharacterId}", _ => {
					var t = new VisualElement();
					var l = new Label($"{capturedBase + capturedBonus}% = {capturedBase}% base + {capturedBonus}% from {capturedInfluence} influence");
					l.AddToClassList("gs-content");
					t.Add(l);
					return t;
				}, new HashSet<string>());
			}

			if (card.TargetCharacterNameKeys != null && card.TargetCharacterNameKeys.Length > 0) {
				string charName = string.Join(" ", System.Linq.Enumerable.Select(card.TargetCharacterNameKeys, k => _loc.Get(k)));
				var targetLabel = new Label(charName);
				targetLabel.AddToClassList("action-card-target");
				result.Body.Insert(1, targetLabel);
			}

			if (card.IsOnCooldown) {
				var overlay = new VisualElement();
				overlay.AddToClassList("action-card-cooldown-overlay");
				var cooldownLabel = new Label(FormatCooldown(card.CooldownEnd, currentTime));
				cooldownLabel.AddToClassList("action-card-cooldown-label");
				overlay.Add(cooldownLabel);
				cardEl.Add(overlay);
			} else if (card.IsUnplayable && !string.IsNullOrEmpty(card.UnplayableReason)) {
				string reasonText = card.UnplayableReason == "pool_full"
					? _loc.Get("action.country.unplayable.pool_full")
					: string.Format(_loc.Get("action.country.unplayable.insufficient_influence"), card.InfluenceThreshold);
				var reasonLabel = new Label(reasonText);
				reasonLabel.AddToClassList("action-card-unplayable-reason");
				cardEl.Add(reasonLabel);
			}

			if (canPlay) {
				string capturedAction = card.ActionId;
				string capturedTarget = card.TargetCharacterId;
				cardEl.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && cardEl.ContainsPoint(e.localPosition)) {
						OnCardClicked?.Invoke(capturedAction, capturedTarget, cardEl);
					}
				});
			}

			wrapper.Add(cardEl);

			if (def != null) {
				var capDef = def;
				_tooltip.RegisterTrigger(wrapper, $"country-action-{card.ActionId}-{card.TargetCharacterId}", _ => {
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

		static string FormatCooldown(DateTime end, DateTime now) {
			TimeSpan remaining = end - now;
			if (remaining <= TimeSpan.Zero) { return "less than a day"; }
			int days = (int)remaining.TotalDays;
			if (days >= 365) { return $"{days / 365} year(s)"; }
			if (days >= 30) { return $"{days / 30} month(s)"; }
			if (days >= 2) { return $"{days} days"; }
			if (days == 1) { return "1 day"; }
			return "less than a day";
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
