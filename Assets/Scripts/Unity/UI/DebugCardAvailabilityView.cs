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
		readonly HashSet<string> _expandedDeckKeys = new();
		readonly HashSet<string> _expandedHandKeys = new();

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

			var groups = new List<DeckActionGroup>();
			var indexByActionId = new Dictionary<string, int>();
			int totalDrawWeight = 0;
			foreach (var card in deck) {
				int drawWeight = GetDrawWeight(card);
				totalDrawWeight += drawWeight;
				if (!indexByActionId.TryGetValue(card.ActionId, out int index)) {
					index = groups.Count;
					indexByActionId[card.ActionId] = index;
					groups.Add(new DeckActionGroup(card.ActionId));
				}
				groups[index].Add(card, drawWeight);
			}

			foreach (var actionGroup in groups) {
				if (actionGroup.UntargetedGroup != null) {
					_deckContainer.Add(BuildDeckCard(actionGroup.UntargetedGroup, totalDrawWeight));
				}

				if (actionGroup.TargetGroups.Count > 0) {
					_deckContainer.Add(BuildTargetedDeckGroup(actionGroup, totalDrawWeight));
				}
			}
		}

		VisualElement BuildDeckCard(DeckGroup group, int totalDrawWeight) {
			double chancePercent = CalculateChancePercent(group.DrawWeight, totalDrawWeight);
			string title = $"{ResolveCardName(group.Representative)} x{group.TotalCount} ({FormatNumber(chancePercent)}%)";
			string actionId = group.Representative.ActionId;
			string targetCountryId = group.Representative.TargetCountryId ?? "";
			string expandKey = $"{actionId}|{targetCountryId}";
			return BuildExpandableCard(
				title,
				expandKey,
				_expandedDeckKeys,
				group.EligibleCount > 0,
				group.Representative.Conditions,
				onDraw: _onDrawDeckCard == null ? null : () => _onDrawDeckCard(actionId, targetCountryId));
		}

		VisualElement BuildTargetedDeckGroup(DeckActionGroup actionGroup, int totalDrawWeight) {
			double chancePercent = CalculateChancePercent(actionGroup.TargetDrawWeight, totalDrawWeight);
			string title = $"{FormatActionId(actionGroup.ActionId)} x{actionGroup.TargetCount} ({FormatNumber(chancePercent)}%)";
			var children = new List<VisualElement>();
			foreach (var targetGroup in actionGroup.TargetGroups) {
				children.Add(BuildDeckCard(targetGroup, totalDrawWeight));
			}

			return BuildExpandableGroup(
				title,
				$"target-group|{actionGroup.ActionId}",
				actionGroup.TargetEligibleCount > 0,
				children);
		}

		// Returns fractional percent (not pre-rounded to a whole number): with many targets
		// (e.g. one relation/revenge card per country) a single destroyed-country card's weight
		// is a small fraction of the group/deck total, and whole-number rounding was hiding that
		// its exclusion actually lowered the group's accumulated chance.
		static double CalculateChancePercent(int drawWeight, int totalDrawWeight) =>
			totalDrawWeight > 0 ? System.Math.Round(100.0 * drawWeight / totalDrawWeight, 2) : 0;

		static string FormatActionId(string actionId) => actionId.Replace('_', ' ');

		int GetDrawWeight(ActionCardEntry card) {
			// Cards targeting a destroyed country are excluded from draw offers
			// (CountryCardDrawQuery.GetDrawableCards) - keep this weight consistent with that,
			// so their shown chance is 0% instead of a stale nonzero share of the deck.
			if (card.UnplayableReason == "country_no_longer_exists") {
				return 0;
			}
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
				string expandKey = $"{actionId}|{targetCountryId}|{slotIndex}";
				_handContainer.Add(BuildExpandableCard(
					title,
					expandKey,
					_expandedHandKeys,
					available,
					conditions,
					onDiscard: _onDiscardHandCard == null
						? null
						: () => _onDiscardHandCard(actionId, targetCountryId, slotIndex)));
			}
		}

		VisualElement BuildExpandableCard(
			string title,
			string expandKey,
			HashSet<string> expandedKeys,
			bool available,
			IReadOnlyList<ActionConditionDebugEntry> conditions,
			Action onDraw = null,
			Action onDiscard = null) {
			var block = new VisualElement();
			block.AddToClassList("debug-card-block");

			var headerRow = new VisualElement();
			headerRow.AddToClassList("debug-card-header-row");

			var header = new Button();
			header.text = $"> {title}";
			header.AddToClassList("gs-btn");
			header.AddToClassList("gs-btn--small");
			header.AddToClassList("debug-panel-button");
			header.AddToClassList(available ? "debug-card-available" : "debug-card-unavailable");
			headerRow.Add(header);

			// Keep Draw/Discard outside the collapsible details so refresh/expand
			// state cannot swallow the click while the game is unpaused.
			if (onDraw != null) {
				headerRow.Add(CreateActionButton("Draw", onDraw));
			}
			if (onDiscard != null) {
				headerRow.Add(CreateActionButton("Discard", onDiscard));
			}

			var details = new VisualElement();
			details.AddToClassList("debug-card-details");
			bool startOpen = expandedKeys.Contains(expandKey);
			details.style.display = startOpen ? DisplayStyle.Flex : DisplayStyle.None;
			header.text = $"{(startOpen ? "v" : ">")} {title}";

			bool hasRows = false;
			if (conditions != null) {
				foreach (var condition in conditions) {
					details.Add(CreateConditionLabel(ActionConditionText.Localize(_loc, condition), condition.Passed));
					hasRows = true;
				}
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
				header.text = $"{(isOpen ? ">" : "v")} {title}";
				if (isOpen) {
					expandedKeys.Remove(expandKey);
				} else {
					expandedKeys.Add(expandKey);
				}
				e.StopPropagation();
			});

			block.Add(headerRow);
			block.Add(details);
			return block;
		}

		VisualElement BuildExpandableGroup(
			string title,
			string expandKey,
			bool available,
			IReadOnlyList<VisualElement> children) {
			var block = new VisualElement();
			block.AddToClassList("debug-card-block");

			var header = new Button();
			header.AddToClassList("gs-btn");
			header.AddToClassList("gs-btn--small");
			header.AddToClassList("debug-panel-button");
			header.AddToClassList(available ? "debug-card-available" : "debug-card-unavailable");

			var details = new VisualElement();
			details.AddToClassList("debug-card-details");
			foreach (var child in children) {
				details.Add(child);
			}

			bool startOpen = _expandedDeckKeys.Contains(expandKey);
			details.style.display = startOpen ? DisplayStyle.Flex : DisplayStyle.None;
			header.text = $"{(startOpen ? "v" : ">")} {title}";
			header.RegisterCallback<PointerUpEvent>(e => {
				if (e.button != 0 || !header.ContainsPoint(e.localPosition)) {
					return;
				}
				bool isOpen = details.style.display != DisplayStyle.None;
				details.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
				header.text = $"{(isOpen ? ">" : "v")} {title}";
				if (isOpen) {
					_expandedDeckKeys.Remove(expandKey);
				} else {
					_expandedDeckKeys.Add(expandKey);
				}
				e.StopPropagation();
			});

			block.Add(header);
			block.Add(details);
			return block;
		}

		static Button CreateActionButton(string text, Action onClick) {
			var button = new Button(() => onClick()) { text = text };
			button.AddToClassList("gs-btn");
			button.AddToClassList("gs-btn--small");
			button.AddToClassList("debug-panel-button");
			button.AddToClassList("debug-card-action-button");
			button.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
			button.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
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

		sealed class DeckActionGroup {
			readonly List<DeckGroup> _targetGroups = new();
			readonly Dictionary<string, int> _targetIndexByCountryId = new();

			public string ActionId { get; }
			public DeckGroup UntargetedGroup { get; private set; }
			public IReadOnlyList<DeckGroup> TargetGroups => _targetGroups;
			public int TargetCount { get; private set; }
			public int TargetEligibleCount { get; private set; }
			public int TargetDrawWeight { get; private set; }

			public DeckActionGroup(string actionId) {
				ActionId = actionId;
			}

			public void Add(ActionCardEntry card, int drawWeight) {
				if (string.IsNullOrEmpty(card.TargetCountryId)) {
					UntargetedGroup ??= new DeckGroup();
					UntargetedGroup.Add(card, drawWeight);
					return;
				}

				if (!_targetIndexByCountryId.TryGetValue(card.TargetCountryId, out int index)) {
					index = _targetGroups.Count;
					_targetIndexByCountryId[card.TargetCountryId] = index;
					_targetGroups.Add(new DeckGroup());
				}
				_targetGroups[index].Add(card, drawWeight);
				TargetCount++;
				TargetDrawWeight += drawWeight;
				if (!card.IsUnplayable) {
					TargetEligibleCount++;
				}
			}
		}
	}
}
