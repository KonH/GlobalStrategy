using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// The action card block, ported from the original single-element GalleryDocument prototype.
	/// Renders with ActionCardBuilder, the very same builder the HUD uses, from a hand-built
	/// ActionCardEntry instead of a running game - no ECS world, no save, required for every
	/// gallery block.
	/// </summary>
	public class ActionCardGalleryBlock : GalleryBlockBase {
		enum CardState {
			Playable,
			UnaffordableGold,
			RequirementsFailed,
			OnCooldown,
			WarOdds,
			MultiCountryTarget,
			DiscardHint
		}

		static readonly List<string> _stateChoices = new List<string> {
			"Playable",
			"Unaffordable gold",
			"Requirements failed",
			"On cooldown",
			"War odds badge",
			"Multi-country target",
			"Discard hint"
		};

		readonly ILocalization _loc;
		readonly ActionConfig _config;
		readonly ActionVisualConfig _actionVisualConfig;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly double _discardGoldCost;
		readonly string _sampleTargetCountryId;
		readonly CountryConfig _countryConfig;
		readonly List<string> _actionIds = new List<string>();

		public override string Id => "action-card";
		public override string Title => "Card";

		protected override IReadOnlyList<string> InstanceChoices => _actionIds;
		protected override IReadOnlyList<string> StateChoices => _stateChoices;
		protected override string InstanceLabel => "Card";

		public ActionCardGalleryBlock(
			ILocalization loc,
			TextAsset actionConfigAsset,
			ActionVisualConfig actionVisualConfig,
			CountryVisualConfig countryVisualConfig,
			double discardGoldCost,
			string sampleTargetCountryId,
			CountryConfig countryConfig) {
			_loc = loc;
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_discardGoldCost = discardGoldCost;
			_sampleTargetCountryId = sampleTargetCountryId;
			_countryConfig = countryConfig;

			if (actionConfigAsset != null) {
				_config = JsonConvert.DeserializeObject<ActionConfig>(actionConfigAsset.text);
			}
			if (_config != null) {
				foreach (ActionDefinition definition in _config.Actions) {
					_actionIds.Add(definition.ActionId);
				}
			}
		}

		protected override void Render(VisualElement stage, string actionId, int stateIndex) {
			if (_config == null) {
				return;
			}
			var cardHost = new VisualElement();
			cardHost.AddToClassList("gallery-card-host");
			stage.Add(cardHost);

			var state = (CardState)Mathf.Clamp(stateIndex, 0, _stateChoices.Count - 1);

			ActionCardEntry entry = BuildEntry(actionId, state);
			ActionCardBuilder.CountryCardFace face = ActionCardBuilder.ComposeFace(
				_loc, _config, _actionVisualConfig, _countryVisualConfig, entry);
			ActionCardBuilder.CardResult result = ActionCardBuilder.Build(face, includeDiscardHint: true);

			result.Card.AddToClassList(entry.CanPlay ? "action-card--available" : "action-card--unavailable");

			if (result.CostLabel != null && state == CardState.UnaffordableGold) {
				result.CostLabel.AddToClassList("action-card-cost-label--unaffordable");
			}
			if (result.DiscardHintLabel != null) {
				result.DiscardHintLabel.text = _loc.Get("action.discard.hint");
			}
			if (result.DiscardHintPrice != null) {
				result.DiscardHintPrice.text = FormatNumber(_discardGoldCost);
			}
			if (result.DiscardHint != null) {
				result.DiscardHint.style.display =
					state == CardState.DiscardHint ? DisplayStyle.Flex : DisplayStyle.None;
			}

			cardHost.Add(result.Card);

			var summary = new Label($"{actionId} - {_stateChoices[(int)state]}");
			summary.AddToClassList("gs-hint");
			summary.AddToClassList("gallery-summary");
			stage.Add(summary);
		}

		ActionCardEntry BuildEntry(string actionId, CardState state) {
			ActionDefinition definition = _config.Find(actionId);
			double goldCost = GetGoldCost(definition);
			double cooldownDays = definition?.CooldownDays ?? 3;

			switch (state) {
				case CardState.UnaffordableGold:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						conditions: new List<ActionConditionDebugEntry> {
							new ActionConditionDebugEntry(
								"gold", false, "action.requirement.gold",
								new[] { FormatNumber(goldCost) })
						});

				case CardState.RequirementsFailed:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						conditions: new List<ActionConditionDebugEntry> {
							new ActionConditionDebugEntry(
								"control", false, "action.requirement.control_min", new[] { "25" }),
							new ActionConditionDebugEntry(
								"opinion", false, "action.requirement.opinion_min_role",
								new[] { _loc.Get("character.role.ruler.name"), "40" }),
							new ActionConditionDebugEntry(
								"capacity", false, "action.requirement.control_capacity")
						});

				case CardState.OnCooldown:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: false,
						cooldownRemainingDays: cooldownDays,
						cooldownFractionRemaining: 0.6);

				case CardState.WarOdds:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: true,
						targetCountryId: _sampleTargetCountryId,
						warWinChancePercent: 42);

				case CardState.MultiCountryTarget:
					return new ActionCardEntry(
						actionId, 0, isInHand: true, canPlay: true,
						playableCountryIds: SampleCountryIds(3));

				case CardState.DiscardHint:
				case CardState.Playable:
				default:
					return new ActionCardEntry(actionId, 0, isInHand: true, canPlay: true);
			}
		}

		List<string> SampleCountryIds(int count) {
			var ids = new List<string>(count);
			if (_countryVisualConfig == null) {
				return ids;
			}
			foreach (CountryVisualEntry entry in _countryVisualConfig.Entries) {
				if (ids.Count >= count) {
					break;
				}
				if (!HudConfigLoader.IsCountryAvailable(_countryConfig, entry.countryId)) {
					continue;
				}
				ids.Add(entry.countryId);
			}
			return ids;
		}

		static double GetGoldCost(ActionDefinition definition) {
			if (definition == null) {
				return 0;
			}
			foreach (ActionCost cost in definition.Cost) {
				if (cost.ResourceId == "gold") {
					return cost.Amount;
				}
			}
			return 0;
		}

		static string FormatNumber(double value) =>
			value.ToString("0.##", CultureInfo.InvariantCulture);
	}
}
