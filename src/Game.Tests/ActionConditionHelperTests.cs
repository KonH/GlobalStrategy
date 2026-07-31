using System.Collections.Generic;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ActionConditionHelperTests {
		static ExpressionNode Value(double v) {
			return new ExpressionNode { Type = "value", Value = v };
		}

		static ExpressionNode Field(string type) {
			return new ExpressionNode { Type = type };
		}

		static ExpressionNode Gte(ExpressionNode left, ExpressionNode right) {
			return new ExpressionNode { Type = "gte", Members = new List<ExpressionNode> { left, right } };
		}

		static ActionDefinition Def(params ExpressionNode[] conditions) {
			return new ActionDefinition { Conditions = new List<ExpressionNode>(conditions) };
		}

		[Fact]
		void gte_condition_on_requested_field_type_returns_true_with_threshold() {
			var def = Def(Gte(Field("opinion"), Value(30)));

			bool found = ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold);

			Assert.True(found);
			Assert.Equal(30, threshold);
		}

		[Fact]
		void different_field_type_returns_false() {
			var def = Def(Gte(Field("control"), Value(5)));

			bool found = ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold);

			Assert.False(found);
		}

		[Fact]
		void non_gte_condition_types_return_false() {
			var def = Def(new ExpressionNode { Type = "eq", Members = new List<ExpressionNode> { Field("opinion"), Value(10) } });

			bool found = ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold);

			Assert.False(found);
		}

		[Fact]
		void empty_conditions_returns_false() {
			var def = Def();

			bool found = ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold);

			Assert.False(found);
		}

		[Fact]
		void genuine_zero_threshold_is_distinguishable_from_not_found() {
			var def = Def(Gte(Field("opinion"), Value(0)));

			bool found = ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold);

			Assert.True(found);
			Assert.Equal(0, threshold);
		}
	}
}
