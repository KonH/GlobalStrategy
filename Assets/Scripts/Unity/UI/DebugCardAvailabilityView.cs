using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class DebugCardAvailabilityView {
		readonly VisualElement _deckContainer;
		readonly VisualElement _handContainer;
		readonly ILocalization _loc;
		readonly ActionConfig _actionConfig;

		public DebugCardAvailabilityView(
			VisualElement deckContainer,
			VisualElement handContainer,
			ILocalization loc,
			ActionConfig actionConfig) {
			_deckContainer = deckContainer;
			_handContainer = handContainer;
			_loc = loc;
			_actionConfig = actionConfig;
		}

		public void RefreshDeck(IReadOnlyList<ActionCardEntry> deck) {
			_deckContainer.Clear();
			if (deck == null || deck.Count == 0) {
				_deckContainer.Add(CreateMutedLabel("(empty)"));
				return;
			}

			int eligibleTotal = 0;
			foreach (var card in deck) {
				if (!card.IsUnplayable) {
					eligibleTotal++;
				}
			}

			var groups = new List<DeckGroup>();
			var indexByKey = new Dictionary<string, int>();
			foreach (var card in deck) {
				string key = $"{card.ActionId}|{card.TargetCountryId}";
				if (!indexByKey.TryGetValue(key, out int index)) {
					index = groups.Count;
					indexByKey[key] = index;
					groups.Add(new DeckGroup(card));
				}
				groups[index].Add(card);
			}

			foreach (var group in groups) {
				int chancePercent = eligibleTotal > 0
					? (int)System.Math.Round(100.0 * group.EligibleCount / eligibleTotal)
					: 0;
				string title = $"{ResolveCardName(group.Representative)} x{group.TotalCount} ({chancePercent}%)";
				bool available = group.EligibleCount > 0;
				_deckContainer.Add(BuildExpandableCard(title, available, group.Representative.Conditions, includeCost: false, goldCost: 0, canAffordGold: true));
			}
		}

		public void RefreshHand(IReadOnlyList<ActionCardEntry> hand, double availableGold) {
			_handContainer.Clear();
			if (hand == null || hand.Count == 0) {
				_handContainer.Add(CreateMutedLabel("(empty)"));
				return;
			}

			foreach (var card in hand) {
				var def = _actionConfig?.Find(card.ActionId);
				double goldCost = GetGoldCost(def);
				bool canAffordGold = goldCost <= 0 || availableGold >= goldCost;
				bool available = !card.IsUnplayable && canAffordGold;
				string title = ResolveCardName(card);
				_handContainer.Add(BuildExpandableCard(title, available, card.Conditions, includeCost: goldCost > 0, goldCost: goldCost, canAffordGold: canAffordGold));
			}
		}

		VisualElement BuildExpandableCard(
			string title,
			bool available,
			IReadOnlyList<ActionConditionDebugEntry> conditions,
			bool includeCost,
			double goldCost,
			bool canAffordGold) {
			var block = new VisualElement();
			block.AddToClassList("debug-card-block");

			var header = new Button();
			header.text = $"▶ {title}";
			header.AddToClassList("gs-btn");
			header.AddToClassList("gs-btn--small");
			header.AddToClassList("debug-panel-button");
			header.AddToClassList(available ? "debug-card-available" : "debug-card-unavailable");

			var details = new VisualElement();
			details.AddToClassList("debug-card-details");
			details.style.display = DisplayStyle.None;

			bool hasRows = false;
			if (conditions != null) {
				foreach (var condition in conditions) {
					details.Add(CreateConditionLabel(condition.Label, condition.Passed));
					hasRows = true;
				}
			}
			if (includeCost) {
				details.Add(CreateConditionLabel($"gold >= {FormatNumber(goldCost)}", canAffordGold));
				hasRows = true;
			}
			if (!hasRows) {
				details.Add(CreateMutedLabel("(no conditions)"));
			}

			header.RegisterCallback<PointerUpEvent>(e => {
				if (e.button != 0 || !header.ContainsPoint(e.localPosition)) {
					return;
				}
				bool isOpen = details.style.display != DisplayStyle.None;
				details.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
				header.text = $"{(isOpen ? "▶" : "▼")} {title}";
			});

			block.Add(header);
			block.Add(details);
			return block;
		}

		string ResolveCardName(ActionCardEntry card) {
			var def = _actionConfig?.Find(card.ActionId);
			if (def == null) {
				return card.ActionId;
			}
			if (!string.IsNullOrEmpty(card.TargetCountryId)) {
				return string.Format(_loc.Get(def.NameKey), _loc.Get($"country_name.{card.TargetCountryId}"));
			}
			return _loc.Get(def.NameKey);
		}

		static Label CreateConditionLabel(string text, bool passed) {
			var label = new Label(text);
			label.AddToClassList("gs-label");
			label.AddToClassList("debug-condition-label");
			label.AddToClassList(passed ? "debug-card-available" : "debug-card-unavailable");
			return label;
		}

		static Label CreateMutedLabel(string text) {
			var label = new Label(text);
			label.AddToClassList("gs-label");
			label.AddToClassList("debug-condition-label");
			return label;
		}

		static double GetGoldCost(ActionDefinition def) {
			if (def == null) {
				return 0;
			}
			foreach (var cost in def.Cost) {
				if (cost.ResourceId == "gold") {
					return cost.Amount;
				}
			}
			return 0;
		}

		static string FormatNumber(double value) {
			return value == System.Math.Floor(value) ? $"{(int)value}" : $"{value:0.##}";
		}

		sealed class DeckGroup {
			public ActionCardEntry Representative { get; private set; }
			public int TotalCount { get; private set; }
			public int EligibleCount { get; private set; }

			public DeckGroup(ActionCardEntry first) {
				Representative = first;
				Add(first);
			}

			public void Add(ActionCardEntry card) {
				TotalCount++;
				if (!card.IsUnplayable) {
					EligibleCount++;
					Representative = card;
				} else if (EligibleCount == 0) {
					Representative = card;
				}
			}
		}
	}
}
