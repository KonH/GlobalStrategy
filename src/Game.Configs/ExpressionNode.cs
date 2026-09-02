using System;
using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ExpressionContext {
		public double Control { get; set; }
		public double TotalCountryControl { get; set; }
		public double Opinion { get; set; }
		public IReadOnlyDictionary<string, double> CountryRelations { get; set; }
			= new Dictionary<string, double>(StringComparer.Ordinal);
		public IReadOnlyDictionary<string, double> Triggers { get; set; }
			= new Dictionary<string, double>(StringComparer.Ordinal);
		public double IsInWar { get; set; }
		public double WarProgress { get; set; }
		public double TargetMilitaryOpinion { get; set; }
		public double NeitherSideAtWar { get; set; }
		public double WarFree { get; set; }
		public double RevengeEligible { get; set; }

		public double GetCountryRelation(string relationKind) {
			if (!CountryRelations.TryGetValue(relationKind, out double value)) {
				throw new InvalidOperationException($"Missing country relation condition value for kind '{relationKind}'.");
			}
			return value;
		}

		public double GetTrigger(string triggerId) {
			if (Triggers.TryGetValue(triggerId, out double value)) {
				return value;
			}
			return 0;
		}
	}

	public class ExpressionNode {
		public string Type { get; set; } = "value";
		public string RelationKind { get; set; } = "";
		public string DesiredRelationKind { get; set; } = "";
		public string TriggerId { get; set; } = "";
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
				case "control": {
					return ctx.Control;
				}
				case "totalCountryControl": {
					return ctx.TotalCountryControl;
				}
				case "opinion": {
					return ctx.Opinion;
				}
				case "hasCountryRelation": {
					ValidateRelationOperand(node);
					return ctx.GetCountryRelation(node.RelationKind);
				}
				case "isInWar": {
					return ctx.IsInWar;
				}
				case "warProgress": {
					return ctx.WarProgress;
				}
				case "targetMilitaryOpinion": {
					return ctx.TargetMilitaryOpinion;
				}
				case "neitherSideAtWar": {
					return ctx.NeitherSideAtWar;
				}
				case "warFree": {
					return ctx.WarFree;
				}
				case "revengeEligible": {
					return ctx.RevengeEligible;
				}
				case "triggerCondition": {
					return ctx.GetTrigger(node.TriggerId);
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

		public static void ValidateRelationOperand(ExpressionNode node) {
			if (node.RelationKind != "none" && node.RelationKind != "friend" && node.RelationKind != "rival") {
				throw new InvalidOperationException(
					$"Expression type 'hasCountryRelation' requires relationKind none|friend|rival, got '{node.RelationKind}'.");
			}
			if (node.RelationKind == "none"
				&& node.DesiredRelationKind != "friend"
				&& node.DesiredRelationKind != "rival") {
				throw new InvalidOperationException(
					$"Expression type 'hasCountryRelation' with relationKind 'none' requires desiredRelationKind friend|rival, got '{node.DesiredRelationKind}'.");
			}
		}
	}
}
