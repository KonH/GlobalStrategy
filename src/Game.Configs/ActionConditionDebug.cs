using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GS.Game.Configs {
	public class ActionConditionDebugEntry {
		public string Label { get; }
		public bool Passed { get; }
		public string LocaleKey { get; }
		public IReadOnlyList<string> LocaleArguments { get; }
		public string ReasonCode { get; }

		public ActionConditionDebugEntry(
			string label,
			bool passed,
			string localeKey = "action.requirement.generic",
			IReadOnlyList<string>? localeArguments = null,
			string reasonCode = "requirement_failed") {
			Label = label;
			Passed = passed;
			LocaleKey = localeKey;
			LocaleArguments = localeArguments ?? Array.Empty<string>();
			ReasonCode = reasonCode;
		}
	}

	public static class ActionConditionDebug {
		public static List<ActionConditionDebugEntry> EvaluateAll(
			IReadOnlyList<ExpressionNode> conditions,
			ExpressionContext ctx,
			string targetRole = "") {
			var results = new List<ActionConditionDebugEntry>();
			if (conditions == null) { return results; }
			foreach (var condition in conditions) {
				results.Add(Evaluate(condition, ctx, targetRole));
			}
			return results;
		}

		public static ActionConditionDebugEntry Evaluate(
			ExpressionNode condition, ExpressionContext ctx, string targetRole = "") {
			bool passed = ExpressionNode.Evaluate(condition, ctx) != 0.0;
			ExpressionNode operand = FindPresentationOperand(condition);
			string threshold = TryGetThreshold(condition, out double value) ? FormatNumber(value) : "";
			string current = FormatNumber(ExpressionNode.Evaluate(operand, ctx));

			switch (operand.Type) {
				case "control":
					return Entry("action.requirement.control_min", "insufficient_control", threshold, current);
				case "totalCountryControl":
					return Entry("action.requirement.enemy_control", "no_enemy_control");
				case "opinion":
					return string.IsNullOrEmpty(targetRole)
						? Entry("action.requirement.opinion_min", "insufficient_opinion", threshold, current)
						: Entry(
							"action.requirement.opinion_min_role", "insufficient_opinion", targetRole, threshold, current);
				case "hasCountryRelation": {
					ExpressionNode.ValidateRelationOperand(operand);
					if (operand.RelationKind == "none") {
						return operand.DesiredRelationKind == "friend"
							? Entry("action.requirement.friend_candidate", "no_friend_candidate")
							: Entry("action.requirement.rival_candidate", "no_rival_candidate");
					}
					return operand.RelationKind == "friend"
						? Entry("action.requirement.relation_friend", "relation_no_longer_exists")
						: Entry("action.requirement.relation_rival", "relation_no_longer_exists");
				}
				case "isInWar":
					return Entry("action.requirement.in_war", "not_at_war");
				case "warProgress":
					return condition.Type == "lte"
						? Entry("action.requirement.war_progress_max", "war_progress_too_high", threshold, current)
						: Entry("action.requirement.war_progress_min", "war_progress_too_low", threshold, current);
				case "targetRulerOrMilitaryOpinion":
					return Entry("action.requirement.target_opinion_min", "insufficient_target_opinion", threshold, current);
				case "neitherSideAtWar":
					return Entry("action.requirement.neither_at_war", "already_at_war");
				case "warFree":
					return Entry("action.requirement.war_free", "at_war");
				case "revengeEligible":
					return Entry("action.requirement.revenge_target", "no_war_loss_to_avenge");
				default:
					return Entry("action.requirement.generic", "requirement_failed");
			}

			ActionConditionDebugEntry Entry(string localeKey, string reasonCode, params string[] arguments) {
				return new ActionConditionDebugEntry(Format(condition, ctx), passed, localeKey, arguments, reasonCode);
			}
		}

		public static string Format(ExpressionNode? node, ExpressionContext ctx) {
			if (node == null) { return "(null)"; }
			switch (node.Type) {
				case "gte":
				case "lte":
				case "gt":
				case "lt":
				case "eq": {
					if (node.Members == null || node.Members.Count < 2) { return node.Type; }
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

		static ExpressionNode FindPresentationOperand(ExpressionNode node) {
			if (IsPresentationOperand(node.Type)) { return node; }
			foreach (ExpressionNode member in node.Members) {
				ExpressionNode found = FindPresentationOperand(member);
				if (IsPresentationOperand(found.Type)) { return found; }
			}
			return node;
		}

		static bool IsPresentationOperand(string type) {
			return type == "control"
				|| type == "totalCountryControl"
				|| type == "opinion"
				|| type == "hasCountryRelation"
				|| type == "isInWar"
				|| type == "warProgress"
				|| type == "targetRulerOrMilitaryOpinion"
				|| type == "neitherSideAtWar"
				|| type == "warFree"
				|| type == "revengeEligible";
		}

		static bool TryGetThreshold(ExpressionNode node, out double value) {
			value = 0;
			if (node.Members.Count < 2 || node.Members[1].Type != "value") { return false; }
			value = node.Members[1].Value;
			return true;
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
				case "hasCountryRelation":
					ExpressionNode.ValidateRelationOperand(node);
					return $"hasCountryRelation[{node.RelationKind}:{node.DesiredRelationKind}] ({FormatNumber(ctx.GetCountryRelation(node.RelationKind))})";
				case "isInWar":
					return $"isInWar ({FormatNumber(ctx.IsInWar)})";
				case "warProgress":
					return $"warProgress ({FormatNumber(ctx.WarProgress)})";
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
					if (node.Members == null || node.Members.Count == 0) { return node.Type; }
					string op = node.Type switch {
						"add" => " + ",
						"sub" => " - ",
						"mul" => " * ",
						_ => " / "
					};
					var sb = new StringBuilder();
					sb.Append('(');
					for (int i = 0; i < node.Members.Count; i++) {
						if (i > 0) { sb.Append(op); }
						sb.Append(FormatOperand(node.Members[i], ctx));
					}
					sb.Append(')');
					return sb.ToString();
				}
				case "clamp": {
					if (node.Members == null || node.Members.Count < 3) { return "clamp"; }
					return $"clamp({FormatOperand(node.Members[0], ctx)}, {FormatOperand(node.Members[1], ctx)}, {FormatOperand(node.Members[2], ctx)})";
				}
				default:
					return node.Type;
			}
		}

		static string FormatNumber(double value) {
			if (value == Math.Floor(value) && !double.IsInfinity(value) && !double.IsNaN(value)) {
				return ((long)value).ToString(CultureInfo.InvariantCulture);
			}
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}
	}
}
