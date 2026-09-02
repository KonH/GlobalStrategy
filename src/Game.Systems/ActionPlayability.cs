using System;
using System.Collections.Generic;
using System.Globalization;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class ActionPlayabilityResult {
		public IReadOnlyList<ActionConditionDebugEntry> Entries { get; }
		public bool CanPlay { get; }
		public ActionConditionDebugEntry? FirstFailure { get; }

		public ActionPlayabilityResult(IReadOnlyList<ActionConditionDebugEntry> entries) {
			Entries = entries;
			foreach (ActionConditionDebugEntry entry in entries) {
				if (!entry.Passed) {
					FirstFailure = entry;
					break;
				}
			}
			CanPlay = FirstFailure == null;
		}

		public static implicit operator bool(ActionPlayabilityResult result) => result.CanPlay;
	}

	public enum ActionPlayabilityGateSet {
		/// <summary>Full playability: authored conditions plus pool/cooldown/gold.</summary>
		All,
		/// <summary>
		/// Intrinsic/hard gates only (relation and war-state conditions). Skips soft gates
		/// such as control, opinion, gold, cooldown, and control-pool capacity.
		/// </summary>
		HardOnly
	}

	public static class ActionPlayability {
		public static ActionPlayabilityResult Evaluate(
			IReadOnlyWorld world,
			ActionConfig config,
			int entity,
			string actionId,
			string orgId,
			string? countryId,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			DateTime currentTime = default,
			int maxControlPool = 100,
			ActionPlayabilityGateSet gateSet = ActionPlayabilityGateSet.All,
			ControlWarSnapshot? snapshot = null) {
			var entries = new List<ActionConditionDebugEntry>();
			ActionDefinition? definition = config.Find(actionId);
			if (definition == null) {
				entries.Add(new ActionConditionDebugEntry(
					$"action exists ({actionId})",
					false,
					"action.requirement.action_exists",
					new[] { actionId },
					"unknown_action"));
				return new ActionPlayabilityResult(entries);
			}

			string selectedCountryId = countryId ?? "";
			string? destroyedTargetId = null;
			if (!string.IsNullOrEmpty(selectedCountryId)
				&& CountryDestroySystem.IsCountryDestroyed(world, selectedCountryId)) {
				destroyedTargetId = selectedCountryId;
			} else if (entity >= 0) {
				string relationTargetId = "";
				if (world.Has<RelationCardTarget>(entity)) {
					relationTargetId = world.Get<RelationCardTarget>(entity).TargetCountryId;
				} else if (world.Has<RevengeCardTarget>(entity)) {
					relationTargetId = world.Get<RevengeCardTarget>(entity).TargetCountryId;
				}
				if (!string.IsNullOrEmpty(relationTargetId)
					&& CountryDestroySystem.IsCountryDestroyed(world, relationTargetId)) {
					destroyedTargetId = relationTargetId;
				}
			}
			if (destroyedTargetId != null) {
				entries.Add(new ActionConditionDebugEntry(
					$"country '{destroyedTargetId}' no longer exists",
					false,
					"action.country.unplayable.country_no_longer_exists",
					new[] { destroyedTargetId },
					"country_no_longer_exists"));
				return new ActionPlayabilityResult(entries);
			}

			ExpressionContext context = CountryActionConditionContext.Build(
				world,
				definition,
				orgId,
				selectedCountryId,
				resources,
				relations,
				entity,
				hqCountryByOrgId,
				gateSet,
				snapshot);
			foreach (ExpressionNode condition in definition.Conditions) {
				if (gateSet == ActionPlayabilityGateSet.HardOnly && IsSoftCondition(condition)) {
					continue;
				}
				entries.Add(ActionConditionDebug.Evaluate(condition, context, definition.TargetRole));
			}

			if (gateSet == ActionPlayabilityGateSet.All) {
				if (actionId == "improve_control") {
					int usedControl = string.IsNullOrEmpty(selectedCountryId)
						? maxControlPool
						: GetTotalControlInCountry(world, snapshot, selectedCountryId);
					bool hasCapacity = usedControl < maxControlPool;
					entries.Add(new ActionConditionDebugEntry(
						$"control pool not full (used {usedControl}/{maxControlPool})",
						hasCapacity,
						"action.requirement.control_capacity",
						new[] { usedControl.ToString(CultureInfo.InvariantCulture), maxControlPool.ToString(CultureInfo.InvariantCulture) },
						"pool_full"));
				}

				TimeSpan? cooldownRemaining = ActionCooldownQuery.GetRemaining(world, orgId, actionId, currentTime);
				entries.Add(new ActionConditionDebugEntry(
					cooldownRemaining.HasValue
						? $"cooldown ready (remaining {cooldownRemaining.Value.TotalDays:0.##} days)"
						: "cooldown ready",
					!cooldownRemaining.HasValue,
					"action.requirement.cooldown_ready",
					cooldownRemaining.HasValue
						? new[] { Math.Ceiling(cooldownRemaining.Value.TotalDays).ToString(CultureInfo.InvariantCulture) }
						: Array.Empty<string>(),
					"on_cooldown"));

				var costOrder = new List<string>();
				var costByResource = new Dictionary<string, double>(StringComparer.Ordinal);
				foreach (ActionCost cost in definition.Cost) {
					if (!costByResource.ContainsKey(cost.ResourceId)) { costOrder.Add(cost.ResourceId); }
					costByResource.TryGetValue(cost.ResourceId, out double total);
					costByResource[cost.ResourceId] = total + cost.Amount;
				}
				foreach (string resourceId in costOrder) {
					double amount = costByResource[resourceId];
					int resourceEntity = resources.FindEntity(world, orgId, resourceId);
					double available = resourceEntity >= 0 ? world.Get<Resource>(resourceEntity).Value : 0.0;
					bool canAfford = available >= amount;
					entries.Add(new ActionConditionDebugEntry(
						$"{resourceId} ({available:0.##}) >= {amount:0.##}",
						canAfford,
						resourceId == ResourceDefinitions.Gold
							? "action.requirement.gold"
							: "action.requirement.resource",
						new[] {
							amount.ToString("0.##", CultureInfo.InvariantCulture),
							available.ToString("0.##", CultureInfo.InvariantCulture),
							resourceId
						},
						"unaffordable"));
				}
			}

			return new ActionPlayabilityResult(entries);
		}

		/// <summary>
		/// Boolean-only fast path with the same gating logic and precomputed-data support as
		/// <see cref="Evaluate"/>, but short-circuits on the first failing condition/cost/cooldown
		/// check and never builds <see cref="ActionConditionDebugEntry"/>/label strings. Use for
		/// hot loops that only need the pass/fail result (e.g. BotObservation's cards x countries
		/// scan) — never for UI/debug display, which needs the full <see cref="Evaluate"/> entries
		/// for tooltips and the debug panel.
		/// </summary>
		public static bool CanPlayFast(
			IReadOnlyWorld world,
			ActionConfig config,
			int entity,
			string actionId,
			string orgId,
			string? countryId,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			DateTime currentTime = default,
			int maxControlPool = 100,
			ActionPlayabilityGateSet gateSet = ActionPlayabilityGateSet.All,
			ControlWarSnapshot? snapshot = null) {
			ActionDefinition? definition = config.Find(actionId);
			if (definition == null) {
				return false;
			}

			string selectedCountryId = countryId ?? "";
			if (!string.IsNullOrEmpty(selectedCountryId)
				&& CountryDestroySystem.IsCountryDestroyed(world, selectedCountryId)) {
				return false;
			}
			if (entity >= 0) {
				string relationTargetId = "";
				if (world.Has<RelationCardTarget>(entity)) {
					relationTargetId = world.Get<RelationCardTarget>(entity).TargetCountryId;
				} else if (world.Has<RevengeCardTarget>(entity)) {
					relationTargetId = world.Get<RevengeCardTarget>(entity).TargetCountryId;
				}
				if (!string.IsNullOrEmpty(relationTargetId)
					&& CountryDestroySystem.IsCountryDestroyed(world, relationTargetId)) {
					return false;
				}
			}

			ExpressionContext context = CountryActionConditionContext.Build(
				world,
				definition,
				orgId,
				selectedCountryId,
				resources,
				relations,
				entity,
				hqCountryByOrgId,
				gateSet,
				snapshot);
			foreach (ExpressionNode condition in definition.Conditions) {
				if (gateSet == ActionPlayabilityGateSet.HardOnly && IsSoftCondition(condition)) {
					continue;
				}
				if (ExpressionNode.Evaluate(condition, context) == 0.0) {
					return false;
				}
			}

			if (gateSet == ActionPlayabilityGateSet.All) {
				if (actionId == "improve_control") {
					int usedControl = string.IsNullOrEmpty(selectedCountryId)
						? maxControlPool
						: GetTotalControlInCountry(world, snapshot, selectedCountryId);
					if (usedControl >= maxControlPool) {
						return false;
					}
				}

				if (ActionCooldownQuery.GetRemaining(world, orgId, actionId, currentTime).HasValue) {
					return false;
				}

				var costByResource = new Dictionary<string, double>(StringComparer.Ordinal);
				foreach (ActionCost cost in definition.Cost) {
					costByResource.TryGetValue(cost.ResourceId, out double total);
					costByResource[cost.ResourceId] = total + cost.Amount;
				}
				foreach (var pair in costByResource) {
					int resourceEntity = resources.FindEntity(world, orgId, pair.Key);
					double available = resourceEntity >= 0 ? world.Get<Resource>(resourceEntity).Value : 0.0;
					if (available < pair.Value) {
						return false;
					}
				}
			}

			return true;
		}

		// Reuses the precomputed ControlWarSnapshot when supplied, instead of rescanning every
		// ControlEffect entity in the world.
		static int GetTotalControlInCountry(IReadOnlyWorld world, ControlWarSnapshot? snapshot, string countryId) {
			return snapshot != null ? snapshot.GetTotalControl(countryId) : ControlQuery.GetTotalControlInCountry(world, countryId);
		}

		static bool IsSoftCondition(ExpressionNode condition) {
			return ContainsSoftOperand(condition);
		}

		static bool ContainsSoftOperand(ExpressionNode node) {
			if (IsSoftOperandType(node.Type)) {
				return true;
			}
			if (node.Members == null) {
				return false;
			}
			foreach (ExpressionNode member in node.Members) {
				if (ContainsSoftOperand(member)) {
					return true;
				}
			}
			return false;
		}

		static bool IsSoftOperandType(string type) {
			return type == "control"
				|| type == "totalCountryControl"
				|| type == "opinion"
				|| type == "targetMilitaryOpinion";
		}

		public static bool CanAfford(IReadOnlyWorld world, string orgId, List<ActionCost> costs, ResourceQuery resources) {
			var totals = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (ActionCost cost in costs) {
				totals.TryGetValue(cost.ResourceId, out double total);
				totals[cost.ResourceId] = total + cost.Amount;
			}
			foreach (var pair in totals) {
				int entity = resources.FindEntity(world, orgId, pair.Key);
				if (entity < 0 || world.Get<Resource>(entity).Value < pair.Value) { return false; }
			}
			return true;
		}
	}
}
