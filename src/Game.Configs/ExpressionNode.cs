using System;
using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ExpressionContext {
		public double Influence { get; set; }
		public double Opinion { get; set; }
	}

	public class ExpressionNode {
		public string Type { get; set; } = "value";
		public double Value { get; set; }
		public List<ExpressionNode> Members { get; set; } = new();

		public static double Evaluate(ExpressionNode? node, ExpressionContext ctx) {
			if (node == null) { return 1.0; }
			switch (node.Type) {
				case "value": {
					return node.Value;
				}
				case "add": {
					double sum = 0;
					foreach (var m in node.Members) { sum += Evaluate(m, ctx); }
					return sum;
				}
				case "sub": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) - Evaluate(node.Members[1], ctx);
				}
				case "mul": {
					double product = 1;
					foreach (var m in node.Members) { product *= Evaluate(m, ctx); }
					return product;
				}
				case "div": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					double denom = Evaluate(node.Members[1], ctx);
					if (Math.Abs(denom) < 1e-12) { return 0; }
					return Evaluate(node.Members[0], ctx) / denom;
				}
				case "clamp": {
					if (node.Members == null || node.Members.Count < 3) { return 0; }
					double v = Evaluate(node.Members[0], ctx);
					double lo = Evaluate(node.Members[1], ctx);
					double hi = Evaluate(node.Members[2], ctx);
					if (v < lo) { return lo; }
					if (v > hi) { return hi; }
					return v;
				}
				case "influence": {
					return ctx.Influence;
				}
				case "opinion": {
					return ctx.Opinion;
				}
				case "gte": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) >= Evaluate(node.Members[1], ctx) ? 1.0 : 0.0;
				}
				case "lte": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) <= Evaluate(node.Members[1], ctx) ? 1.0 : 0.0;
				}
				case "gt": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) > Evaluate(node.Members[1], ctx) ? 1.0 : 0.0;
				}
				case "lt": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) < Evaluate(node.Members[1], ctx) ? 1.0 : 0.0;
				}
				case "eq": {
					if (node.Members == null || node.Members.Count < 2) { return 0; }
					return Evaluate(node.Members[0], ctx) == Evaluate(node.Members[1], ctx) ? 1.0 : 0.0;
				}
				default: {
					return node.Value;
				}
			}
		}
	}
}
