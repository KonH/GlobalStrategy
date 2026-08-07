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
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			DateTime currentTime = default,
			int maxControlPool = 100,
			ActionPlayabilityGateSet gateSet = ActionPlayabilityGateSet.All) {
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
			ExpressionContext context = CountryActionConditionContext.Build(
				world,
				definition,
				orgId,
				selectedCountryId,
				entity,
				hqCountryByOrgId);
			foreach (ExpressionNode condition in definition.Conditions) {
				if (gateSet == ActionPlayabilityGateSet.HardOnly && IsSoftCondition(condition)) {
					continue;
				}
				entries.Add(ActionConditionDebug.Evaluate(condition, context));
			}

			if (gateSet == ActionPlayabilityGateSet.All) {
				if (actionId == "improve_control") {
					int usedControl = string.IsNullOrEmpty(selectedCountryId)
						? maxControlPool
						: ControlQuery.GetTotalControlInCountry(world, selectedCountryId);
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
					int resourceEntity = FindResourceEntity(world, orgId, resourceId);
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
				|| type == "targetRulerOrMilitaryOpinion";
		}

		public static bool CanAfford(IReadOnlyWorld world, string orgId, List<ActionCost> costs) {
			var totals = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (ActionCost cost in costs) {
				totals.TryGetValue(cost.ResourceId, out double total);
				totals[cost.ResourceId] = total + cost.Amount;
			}
			foreach (var pair in totals) {
				int entity = FindResourceEntity(world, orgId, pair.Key);
				if (entity < 0 || world.Get<Resource>(entity).Value < pair.Value) { return false; }
			}
			return true;
		}

		public static int FindResourceEntity(IReadOnlyWorld world, string ownerId, string resourceId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
