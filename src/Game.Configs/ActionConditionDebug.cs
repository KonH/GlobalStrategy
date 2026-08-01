using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GS.Game.Configs {
	public class ActionConditionDebugEntry {
		public string Label { get; }
		public bool Passed { get; }

		public ActionConditionDebugEntry(string label, bool passed) {
			Label = label;
			Passed = passed;
		}
	}

	public static class ActionConditionDebug {
		public static List<ActionConditionDebugEntry> EvaluateAll(
			IReadOnlyList<ExpressionNode> conditions,
			ExpressionContext ctx) {
			var results = new List<ActionConditionDebugEntry>();
			if (conditions == null) {
				return results;
			}
			foreach (var cond in conditions) {
				bool passed = ExpressionNode.Evaluate(cond, ctx) != 0.0;
				results.Add(new ActionConditionDebugEntry(Format(cond, ctx), passed));
			}
			return results;
		}

		public static string Format(ExpressionNode? node, ExpressionContext ctx) {
			if (node == null) {
				return "(null)";
			}
			switch (node.Type) {
				case "gte":
				case "lte":
				case "gt":
				case "lt":
				case "eq": {
					if (node.Members == null || node.Members.Count < 2) {
						return node.Type;
					}
					string op = node.Type switch {
						"gte" => ">=",
						"lte" => "<=",
						"gt" => ">",
						"lt" => "<",
						_ => "=="
					};
					return $"{FormatOperand(node.Members[0], ctx)} {op} {FormatOperand(node.Members[1], ctx)}";
				}
				default:
					return FormatOperand(node, ctx);
			}
		}

		static string FormatOperand(ExpressionNode node, ExpressionContext ctx) {
			switch (node.Type) {
				case "value":
					return FormatNumber(node.Value);
				case "control":
					return $"control ({FormatNumber(ctx.Control)})";
				case "totalCountryControl":
					return $"totalCountryControl ({FormatNumber(ctx.TotalCountryControl)})";
				case "opinion":
					return $"opinion ({FormatNumber(ctx.Opinion)})";
				case "hasSuitableRelationTarget":
					return $"hasSuitableRelationTarget ({FormatNumber(ctx.HasSuitableRelationTarget)})";
				case "relationStillExists":
					return $"relationStillExists ({FormatNumber(ctx.RelationStillExists)})";
				case "targetRulerOrMilitaryOpinion":
					return $"targetRulerOrMilitaryOpinion ({FormatNumber(ctx.TargetRulerOrMilitaryOpinion)})";
				case "neitherSideAtWar":
					return $"neitherSideAtWar ({FormatNumber(ctx.NeitherSideAtWar)})";
				case "warFree":
					return $"warFree ({FormatNumber(ctx.WarFree)})";
				case "revengeEligible":
					return $"revengeEligible ({FormatNumber(ctx.RevengeEligible)})";
				case "add":
				case "sub":
				case "mul":
				case "div": {
					if (node.Members == null || node.Members.Count == 0) {
						return node.Type;
					}
					string op = node.Type switch {
						"add" => " + ",
						"sub" => " - ",
						"mul" => " * ",
						_ => " / "
					};
					var sb = new StringBuilder();
					sb.Append('(');
					for (int i = 0; i < node.Members.Count; i++) {
						if (i > 0) {
							sb.Append(op);
						}
						sb.Append(FormatOperand(node.Members[i], ctx));
					}
					sb.Append(')');
					return sb.ToString();
				}
				case "clamp": {
					if (node.Members == null || node.Members.Count < 3) {
						return "clamp";
					}
					return $"clamp({FormatOperand(node.Members[0], ctx)}, {FormatOperand(node.Members[1], ctx)}, {FormatOperand(node.Members[2], ctx)})";
				}
				default:
					return node.Type;
			}
		}

		static string FormatNumber(double value) {
			if (value == System.Math.Floor(value) && !double.IsInfinity(value) && !double.IsNaN(value)) {
				return ((long)value).ToString(CultureInfo.InvariantCulture);
			}
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}
	}
}
