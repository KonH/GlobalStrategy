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
		readonly Action<string, string> _onDrawDeckCard;
		readonly Action<string, string, int> _onDiscardHandCard;

		public DebugCardAvailabilityView(
			VisualElement deckContainer,
			VisualElement handContainer,
			ILocalization loc,
			ActionConfig actionConfig,
			Action<string, string> onDrawDeckCard = null,
			Action<string, string, int> onDiscardHandCard = null) {
			_deckContainer = deckContainer;
			_handContainer = handContainer;
			_loc = loc;
			_actionConfig = actionConfig;
			_onDrawDeckCard = onDrawDeckCard;
			_onDiscardHandCard = onDiscardHandCard;
		}

		public void RefreshDeck(IReadOnlyList<ActionCardEntry> deck) {
			_deckContainer.Clear();
			if (deck == null || deck.Count == 0) {
				_deckContainer.Add(CreateMutedLabel("(empty)"));
				return;
			}

			var groups = new List<DeckGroup>();
			var indexByKey = new Dictionary<string, int>();
			int totalDrawWeight = 0;
			foreach (var card in deck) {
				int drawWeight = GetDrawWeight(card);
				totalDrawWeight += drawWeight;
				string key = $"{card.ActionId}|{card.TargetCountryId}";
				if (!indexByKey.TryGetValue(key, out int index)) {
					index = groups.Count;
					indexByKey[key] = index;
					groups.Add(new DeckGroup());
				}
				groups[index].Add(card, drawWeight);
			}

			foreach (var group in groups) {
				int chancePercent = totalDrawWeight > 0
					? (int)System.Math.Round(100.0 * group.DrawWeight / totalDrawWeight)
					: 0;
				string title = $"{ResolveCardName(group.Representative)} x{group.TotalCount} ({chancePercent}%)";
				bool available = group.EligibleCount > 0;
				string actionId = group.Representative.ActionId;
				string targetCountryId = group.Representative.TargetCountryId ?? "";
				_deckContainer.Add(BuildExpandableCard(
					title,
					available,
					group.Representative.Conditions,
					onDraw: _onDrawDeckCard == null ? null : () => _onDrawDeckCard(actionId, targetCountryId)));
			}
		}

		int GetDrawWeight(ActionCardEntry card) {
			ActionDefinition definition = _actionConfig?.Find(card.ActionId);
			return definition != null && definition.DeckCopies > 0 ? definition.DeckCopies : 0;
		}

		public void RefreshHand(IReadOnlyList<ActionCardEntry> hand, double availableGold) {
			_handContainer.Clear();
			if (hand == null || hand.Count == 0) {
				_handContainer.Add(CreateMutedLabel("(empty)"));
				return;
			}

			foreach (var card in hand) {
				var conditions = new List<ActionConditionDebugEntry>(card.Conditions);
				bool hasProjectedGold = false;
				foreach (var condition in conditions) {
					if (condition.LocaleKey == "action.requirement.gold") {
						hasProjectedGold = true;
						break;
					}
				}
				bool canAffordLegacyCost = true;
				if (!hasProjectedGold) {
					double goldCost = GetGoldCost(_actionConfig?.Find(card.ActionId));
					if (goldCost > 0) {
						canAffordLegacyCost = availableGold >= goldCost;
						conditions.Add(new ActionConditionDebugEntry(
							$"gold ({FormatNumber(availableGold)}) >= {FormatNumber(goldCost)}",
							canAffordLegacyCost,
							"action.requirement.gold",
							new[] { FormatNumber(goldCost), FormatNumber(availableGold), "gold" },
							"unaffordable"));
					}
				}
				bool available = card.CanPlay && canAffordLegacyCost;
				string title = ResolveCardName(card);
				string actionId = card.ActionId;
				string targetCountryId = card.TargetCountryId ?? "";
				int slotIndex = card.SlotIndex;
				_handContainer.Add(BuildExpandableCard(
					title,
					available,
					conditions,
					onDiscard: _onDiscardHandCard == null
						? null
						: () => _onDiscardHandCard(actionId, targetCountryId, slotIndex)));
			}
		}

		VisualElement BuildExpandableCard(
			string title,
			bool available,
			IReadOnlyList<ActionConditionDebugEntry> conditions,
			Action onDraw = null,
			Action onDiscard = null) {
			var block = new VisualElement();
			block.AddToClassList("debug-card-block");

			var header = new Button();
			header.text = $"> {title}";
			header.AddToClassList("gs-btn");
			header.AddToClassList("gs-btn--small");
			header.AddToClassList("debug-panel-button");
			header.AddToClassList(available ? "debug-card-available" : "debug-card-unavailable");

			var details = new VisualElement();
			details.AddToClassList("debug-card-details");
			details.style.display = DisplayStyle.None;

			if (onDraw != null) {
				details.Add(CreateActionButton("Draw", onDraw));
			}
			if (onDiscard != null) {
				details.Add(CreateActionButton("Discard", onDiscard));
			}

			bool hasRows = false;
			if (conditions != null) {
				foreach (var condition in conditions) {
					details.Add(CreateConditionLabel(ActionConditionText.Localize(_loc, condition), condition.Passed));
					hasRows = true;
				}
			}
			if (!hasRows && onDraw == null && onDiscard == null) {
				details.Add(CreateMutedLabel("(no conditions)"));
			}

			header.RegisterCallback<PointerUpEvent>(e => {
				if (e.button != 0 || !header.ContainsPoint(e.localPosition)) {
					return;
				}
				bool isOpen = details.style.display != DisplayStyle.None;
				details.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
				header.text = $"{(isOpen ? ">" : "v")} {title}";
			});

			block.Add(header);
			block.Add(details);
			return block;
		}

		static Button CreateActionButton(string text, Action onClick) {
			var button = new Button(onClick) { text = text };
			button.AddToClassList("gs-btn");
			button.AddToClassList("gs-btn--small");
			button.AddToClassList("debug-panel-button");
			return button;
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

		static string FormatNumber(double value) =>
			value == System.Math.Floor(value) ? $"{(int)value}" : $"{value:0.##}";

		sealed class DeckGroup {
			public ActionCardEntry Representative { get; private set; }
			public int TotalCount { get; private set; }
			public int EligibleCount { get; private set; }
			public int DrawWeight { get; private set; }

			public void Add(ActionCardEntry card, int drawWeight) {
				TotalCount++;
				DrawWeight += drawWeight;
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
