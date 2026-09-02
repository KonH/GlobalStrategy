using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	// Shared card-entry construction used by both the still-per-tick UpdateCountryActions (the
	// currently selected country's hand/deck, gated by CountryActionsVisibility) and the
	// pull-only DebugOrgCardAvailabilityProjector (the debug menu's "My org"/"Selected org"
	// listings). Extracted so neither caller needs a VisualStateConverter instance.
	public static class ActionCardEntryProjector {
		// No ActionPlayability evaluation at all - just enough for callers that only ever read
		// .Count / .ActionId (deck pile, and hand while its detail panel is collapsed).
		public static ActionCardEntry? BuildCheapEntry(ActionConfig? actionConfig, string actionId, int slotIndex, bool isInHand) {
			return actionConfig?.Find(actionId) == null ? null : new ActionCardEntry(actionId, slotIndex, isInHand);
		}

		public static ActionCardEntry? BuildEntry(
			IReadOnlyWorld world,
			ActionConfig? actionConfig,
			EffectConfig? effectConfig,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string> hqCountryByOrgId,
			int maxControlPool,
			string orgId, string countryId, int entity,
			string actionId, int slotIndex, bool isInHand,
			bool includePlayableCountryIds,
			DateTime currentTime,
			IReadOnlyList<string> playableCountryOrder,
			ControlWarSnapshot snapshot) {
			var def = actionConfig?.Find(actionId);
			if (def == null) { return null; }

			string targetCountryId = world.Has<RelationCardTarget>(entity)
				? world.Get<RelationCardTarget>(entity).TargetCountryId
				: world.Has<RevengeCardTarget>(entity) ? world.Get<RevengeCardTarget>(entity).TargetCountryId : "";

			string? destroyedCountryId = null;
			if (!string.IsNullOrEmpty(countryId) && CountryDestroySystem.IsCountryDestroyed(world, countryId)) {
				destroyedCountryId = countryId;
			} else if (!string.IsNullOrEmpty(targetCountryId)
				&& CountryDestroySystem.IsCountryDestroyed(world, targetCountryId)) {
				destroyedCountryId = targetCountryId;
			}
			if (destroyedCountryId != null) {
				var destroyedFailure = new ActionConditionDebugEntry(
					$"country '{destroyedCountryId}' no longer exists",
					false,
					"action.country.unplayable.country_no_longer_exists",
					new[] { destroyedCountryId },
					"country_no_longer_exists");
				string entryTargetCountryId = !string.IsNullOrEmpty(targetCountryId)
					? targetCountryId
					: destroyedCountryId;
				return new ActionCardEntry(
					actionId, slotIndex, isInHand, true,
					"country_no_longer_exists", entryTargetCountryId,
					new List<ActionConditionDebugEntry> { destroyedFailure },
					null, null, null,
					false, destroyedFailure, Array.Empty<string>());
			}

			ActionPlayabilityResult playability = ActionPlayability.Evaluate(
				world, actionConfig!, entity, actionId, orgId, countryId,
				resources, relations, hqCountryByOrgId, currentTime, maxControlPool,
				ActionPlayabilityGateSet.All, snapshot);
			TimeSpan? remaining = ActionCooldownQuery.GetRemaining(world, orgId, actionId, currentTime);
			bool onCooldown = remaining.HasValue;
			double? cooldownRemainingDays = onCooldown ? Math.Ceiling(remaining!.Value.TotalDays) : (double?)null;
			double? cooldownFractionRemaining = onCooldown && def.CooldownDays > 0
				? Math.Round(Math.Clamp(remaining!.Value.TotalDays / def.CooldownDays, 0.0, 1.0), 2)
				: (double?)null;
			string countryContextId = world.Has<CountryContext>(entity)
				? world.Get<CountryContext>(entity).CountryId
				: "";

			int? warWinChancePercent = null;
			if ((actionId == "declare_war" || actionId == "declare_revenge_war") && !string.IsNullOrEmpty(targetCountryId)) {
				double pendingDamageBonusPercent = 0;
				double pendingDurabilityBonusPercent = 0;
				if (actionId == "declare_revenge_war") {
					TryResolveRevengePendingBonuses(actionConfig, effectConfig, actionId, out pendingDamageBonusPercent, out pendingDurabilityBonusPercent);
				}
				warWinChancePercent = WarWinChanceEstimator.EstimateAttackerWinPercent(
					world,
					resources,
					countryId,
					targetCountryId,
					pendingDamageBonusPercent,
					pendingDurabilityBonusPercent);
			}

			var playableCountryIds = new List<string>();
			if (includePlayableCountryIds && !playability.CanPlay) {
				foreach (string candidateCountryId in playableCountryOrder) {
					if (ActionPlayability.CanPlayFast(
						world, actionConfig!, entity, actionId, orgId, candidateCountryId,
						resources, relations, hqCountryByOrgId, currentTime, maxControlPool,
						ActionPlayabilityGateSet.HardOnly, snapshot)) {
						playableCountryIds.Add(candidateCountryId);
					}
				}
			}

			return new ActionCardEntry(
				actionId, slotIndex, isInHand, !playability.CanPlay,
				playability.FirstFailure?.ReasonCode ?? "", targetCountryId, playability.Entries,
				warWinChancePercent, cooldownRemainingDays, cooldownFractionRemaining,
				playability.CanPlay, playability.FirstFailure, playableCountryIds, countryContextId);
		}

		static bool TryResolveRevengePendingBonuses(
			ActionConfig? actionConfig, EffectConfig? effectConfig, string actionId,
			out double damageBonusPercent, out double durabilityBonusPercent) {
			damageBonusPercent = 0;
			durabilityBonusPercent = 0;
			var def = actionConfig?.Find(actionId);
			if (def == null || effectConfig == null) {
				return false;
			}
			foreach (string effectId in def.EffectIds) {
				var effect = effectConfig.Find(effectId);
				if (effect is DeclareRevengeWarEffectParams revengeParams) {
					damageBonusPercent = revengeParams.DamageBonusPercent;
					durabilityBonusPercent = revengeParams.DurabilityBonusPercent;
					return true;
				}
			}
			return false;
		}
	}
}
