using System.Collections.Generic;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CharacterCardHintProjectorTests {
		static ExpressionNode Value(double v) {
			return new ExpressionNode { Type = "value", Value = v };
		}

		static ExpressionNode Field(string type) {
			return new ExpressionNode { Type = type };
		}

		static ExpressionNode Gte(ExpressionNode left, ExpressionNode right) {
			return new ExpressionNode { Type = "gte", Members = new List<ExpressionNode> { left, right } };
		}

		static ActionDefinition OpinionGatedAction(string actionId, string roleId, int threshold) {
			return new ActionDefinition {
				ActionId = actionId,
				TargetRole = roleId,
				Conditions = new List<ExpressionNode> { Gte(Field("opinion"), Value(threshold)) }
			};
		}

		static ActionConfig Config(params ActionDefinition[] actions) {
			return new ActionConfig { Actions = new List<ActionDefinition>(actions) };
		}

		[Fact]
		void role_with_one_matching_opinion_gated_action_yields_single_row() {
			var config = Config(OpinionGatedAction("card_1", "secret_advisor", 30));

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Single(rows);
			Assert.Equal("card_1", rows[0].ActionId);
			Assert.Equal(30, rows[0].Threshold);
		}

		[Fact]
		void role_with_no_actions_yields_empty_list() {
			var config = Config();

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Empty(rows);
		}

		[Fact]
		void role_with_only_control_gated_action_yields_empty_list() {
			var def = new ActionDefinition {
				ActionId = "card_1",
				TargetRole = "secret_advisor",
				Conditions = new List<ExpressionNode> { Gte(Field("control"), Value(5)) }
			};
			var config = Config(def);

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Empty(rows);
		}

		[Fact]
		void actions_for_a_different_target_role_are_excluded() {
			var config = Config(OpinionGatedAction("card_1", "other_role", 30));

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Empty(rows);
		}

		[Fact]
		void multiple_gated_actions_are_ordered_ascending_by_threshold() {
			var config = Config(
				OpinionGatedAction("card_50", "secret_advisor", 50),
				OpinionGatedAction("card_30", "secret_advisor", 30));

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Equal(2, rows.Count);
			Assert.Equal("card_30", rows[0].ActionId);
			Assert.Equal(30, rows[0].Threshold);
			Assert.Equal("card_50", rows[1].ActionId);
			Assert.Equal(50, rows[1].Threshold);
		}

		[Fact]
		void actions_sharing_the_same_threshold_both_appear_in_config_order() {
			var config = Config(
				OpinionGatedAction("card_a", "secret_advisor", 30),
				OpinionGatedAction("card_b", "secret_advisor", 30));

			var rows = CharacterCardHintProjector.Build(config, "secret_advisor");

			Assert.Equal(2, rows.Count);
			Assert.Equal("card_a", rows[0].ActionId);
			Assert.Equal("card_b", rows[1].ActionId);
		}
	}
}
