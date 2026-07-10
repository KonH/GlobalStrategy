using System.Collections.Generic;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ExpressionNodeTests {
		static ExpressionNode Value(double v) =>
			new ExpressionNode { Type = "value", Value = v };

		static ExpressionNode Control() =>
			new ExpressionNode { Type = "control" };

		static ExpressionNode Node(string type, params ExpressionNode[] members) {
			var n = new ExpressionNode { Type = type, Members = new List<ExpressionNode>(members) };
			return n;
		}

		static ExpressionContext Ctx(double control = 0, double opinion = 0) =>
			new ExpressionContext { Control = control, Opinion = opinion };

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
		public void control_node_returns_context_control() {
			var node = Control();
			Assert.Equal(15.0, ExpressionNode.Evaluate(node, Ctx(control: 15)));
		}

		[Fact]
		public void gte_node_true_returns_one() {
			var node = Node("gte", Control(), Value(10));
			Assert.Equal(1.0, ExpressionNode.Evaluate(node, Ctx(control: 10)));
		}

		[Fact]
		public void gte_node_false_returns_zero() {
			var node = Node("gte", Control(), Value(10));
			Assert.Equal(0.0, ExpressionNode.Evaluate(node, Ctx(control: 9)));
		}

		[Fact]
		public void composite_success_rate() {
			// add(value(0.3), div(control, value(100))) with control=20 → 0.3 + 0.2 = 0.5
			var node = Node("add", Value(0.3), Node("div", Control(), Value(100)));
			Assert.Equal(0.5, ExpressionNode.Evaluate(node, Ctx(control: 20)), precision: 10);
		}

		[Fact]
		public void clamp_node() {
			// clamp(add(value(0.3), div(control, value(2))), value(0), value(1)) with control=100 → clamped to 1
			var node = Node("clamp",
				Node("add", Value(0.3), Node("div", Control(), Value(2))),
				Value(0), Value(1));
			Assert.Equal(1.0, ExpressionNode.Evaluate(node, Ctx(control: 100)));
		}
	}
}
