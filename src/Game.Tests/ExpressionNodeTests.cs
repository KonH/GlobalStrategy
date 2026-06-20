using System.Collections.Generic;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ExpressionNodeTests {
		static ExpressionNode Value(double v) =>
			new ExpressionNode { Type = "value", Value = v };

		static ExpressionNode Influence() =>
			new ExpressionNode { Type = "influence" };

		static ExpressionNode Node(string type, params ExpressionNode[] members) {
			var n = new ExpressionNode { Type = type, Members = new List<ExpressionNode>(members) };
			return n;
		}

		static ExpressionContext Ctx(double influence = 0, double opinion = 0) =>
			new ExpressionContext { Influence = influence, Opinion = opinion };

		[Fact]
		public void value_node_returns_literal() {
			var node = Value(0.5);
			Assert.Equal(0.5, ExpressionNode.Evaluate(node, Ctx()));
		}

		[Fact]
		public void add_node_sums_members() {
			var node = Node("add", Value(0.3), Value(0.2));
			Assert.Equal(0.5, ExpressionNode.Evaluate(node, Ctx()), precision: 10);
		}

		[Fact]
		public void div_node_returns_quotient() {
			var node = Node("div", Value(10), Value(2));
			Assert.Equal(5.0, ExpressionNode.Evaluate(node, Ctx()));
		}

		[Fact]
		public void div_node_zero_denominator_returns_zero() {
			var node = Node("div", Value(1), Value(0));
			Assert.Equal(0.0, ExpressionNode.Evaluate(node, Ctx()));
		}

		[Fact]
		public void influence_node_returns_context_influence() {
			var node = Influence();
			Assert.Equal(15.0, ExpressionNode.Evaluate(node, Ctx(influence: 15)));
		}

		[Fact]
		public void gte_node_true_returns_one() {
			var node = Node("gte", Influence(), Value(10));
			Assert.Equal(1.0, ExpressionNode.Evaluate(node, Ctx(influence: 10)));
		}

		[Fact]
		public void gte_node_false_returns_zero() {
			var node = Node("gte", Influence(), Value(10));
			Assert.Equal(0.0, ExpressionNode.Evaluate(node, Ctx(influence: 9)));
		}

		[Fact]
		public void composite_success_rate() {
			// add(value(0.3), div(influence, value(100))) with influence=20 → 0.3 + 0.2 = 0.5
			var node = Node("add", Value(0.3), Node("div", Influence(), Value(100)));
			Assert.Equal(0.5, ExpressionNode.Evaluate(node, Ctx(influence: 20)), precision: 10);
		}

		[Fact]
		public void clamp_node() {
			// clamp(add(value(0.3), div(influence, value(2))), value(0), value(1)) with influence=100 → clamped to 1
			var node = Node("clamp",
				Node("add", Value(0.3), Node("div", Influence(), Value(2))),
				Value(0), Value(1));
			Assert.Equal(1.0, ExpressionNode.Evaluate(node, Ctx(influence: 100)));
		}
	}
}
