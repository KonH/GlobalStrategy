using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Game.Tests.Helpers {
	/// <summary>
	/// Terse builders for <see cref="ExpressionNode"/> trees, replacing the deeply nested
	/// object-initializer literals that action/condition tests would otherwise repeat.
	/// </summary>
	public static class Expr {
		public static ExpressionNode Value(double value) {
			return new ExpressionNode { Type = "value", Value = value };
		}

		/// <summary>Context field node, e.g. "control", "opinion", "warProgress".</summary>
		public static ExpressionNode Field(string type) {
			return new ExpressionNode { Type = type };
		}

		public static ExpressionNode Node(string type, params ExpressionNode[] members) {
			return new ExpressionNode { Type = type, Members = new List<ExpressionNode>(members) };
		}

		public static ExpressionNode Relation(string relationKind, string desiredRelationKind = "") {
			return new ExpressionNode {
				Type = "hasCountryRelation",
				RelationKind = relationKind,
				DesiredRelationKind = desiredRelationKind
			};
		}

		public static ExpressionNode Trigger(string triggerId) {
			return new ExpressionNode { Type = "trigger", TriggerId = triggerId };
		}

		public static ExpressionNode Add(params ExpressionNode[] members) => Node("add", members);
		public static ExpressionNode Sub(ExpressionNode left, ExpressionNode right) => Node("sub", left, right);
		public static ExpressionNode Mul(params ExpressionNode[] members) => Node("mul", members);

		public static ExpressionNode Gte(ExpressionNode left, ExpressionNode right) => Node("gte", left, right);
		public static ExpressionNode Gt(ExpressionNode left, ExpressionNode right) => Node("gt", left, right);
		public static ExpressionNode Lte(ExpressionNode left, ExpressionNode right) => Node("lte", left, right);
		public static ExpressionNode Lt(ExpressionNode left, ExpressionNode right) => Node("lt", left, right);
		public static ExpressionNode Eq(ExpressionNode left, ExpressionNode right) => Node("eq", left, right);

		/// <summary>Shorthand for the overwhelmingly common "field &gt;= constant" condition.</summary>
		public static ExpressionNode Gte(string fieldType, double value) => Gte(Field(fieldType), Value(value));
		public static ExpressionNode Gt(string fieldType, double value) => Gt(Field(fieldType), Value(value));
		public static ExpressionNode Lte(string fieldType, double value) => Lte(Field(fieldType), Value(value));
		public static ExpressionNode Lt(string fieldType, double value) => Lt(Field(fieldType), Value(value));

		/// <summary>Shorthand for "hasCountryRelation(kind, desired) &gt;= 1".</summary>
		public static ExpressionNode HasRelation(string relationKind, string desiredRelationKind = "") {
			return Gte(Relation(relationKind, desiredRelationKind), Value(1));
		}
	}
}
