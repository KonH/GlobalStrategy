using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Game.Tests.Helpers {
	/// <summary>
	/// Fluent builder for the small <see cref="ActionConfig"/> fixtures tests need.
	/// Pair with <see cref="Expr"/> for condition trees.
	/// </summary>
	public sealed class TestActionConfig {
		readonly ActionConfig _config = new ActionConfig();

		public static TestActionConfig Create() {
			return new TestActionConfig();
		}

		public TestActionConfig HandSize(string ownerType, int handSize) {
			_config.Defaults.Add(new ActionOwnerDefaults { OwnerType = ownerType, HandSize = handSize });
			return this;
		}

		public TestActionConfig Action(
			string actionId,
			string ownerType = "country",
			string targetRole = "",
			double chance = 3.0,
			double cooldownDays = 0,
			string rarity = "Standard",
			double? drawWeightMultiplier = null,
			IEnumerable<ExpressionNode>? conditions = null,
			IEnumerable<ActionCost>? cost = null,
			IEnumerable<string>? effectIds = null) {
			_config.Actions.Add(new ActionDefinition {
				ActionId = actionId,
				OwnerType = ownerType,
				TargetRole = targetRole,
				Chance = chance,
				CooldownDays = cooldownDays,
				Rarity = rarity,
				DrawWeightMultiplier = drawWeightMultiplier,
				Conditions = conditions == null ? new List<ExpressionNode>() : new List<ExpressionNode>(conditions),
				Cost = cost == null ? new List<ActionCost>() : new List<ActionCost>(cost),
				EffectIds = effectIds == null ? new List<string>() : new List<string>(effectIds)
			});
			return this;
		}

		/// <summary>
		/// Adds a country-owned action whose only non-default field is its condition list.
		/// The <c>ownerType</c> is passed positionally so overload resolution cannot pick this
		/// <c>params</c> overload again and recurse.
		/// </summary>
		public TestActionConfig Action(string actionId, params ExpressionNode[] conditions) {
			return Action(actionId, "country", conditions: conditions);
		}

		public TestActionConfig OrgPool(string orgId, params string[] actionIds) {
			_config.OrgPools.Add(new OrgActionPool { OrgId = orgId, ActionIds = new List<string>(actionIds) });
			return this;
		}

		/// <summary>Mutate the definition added last — for the rare field this builder does not surface.</summary>
		public TestActionConfig Tweak(System.Action<ActionDefinition> mutate) {
			if (_config.Actions.Count == 0) {
				throw new System.InvalidOperationException("Tweak called before any Action was added.");
			}
			mutate(_config.Actions[_config.Actions.Count - 1]);
			return this;
		}

		public ActionConfig Build() {
			return _config;
		}

		public static implicit operator ActionConfig(TestActionConfig builder) => builder.Build();
	}
}
