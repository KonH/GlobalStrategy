using System.Collections.Generic;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ActionConditionDebugTests {
		[Fact]
		void evaluate_all_formats_and_colors_each_condition() {
			var conditions = new List<ExpressionNode> {
				new ExpressionNode {
					Type = "gte",
					Members = {
						new ExpressionNode { Type = "targetRulerOrMilitaryOpinion" },
						new ExpressionNode { Type = "value", Value = 50 }
					}
				},
				new ExpressionNode {
					Type = "gte",
					Members = {
						new ExpressionNode { Type = "relationStillExists" },
						new ExpressionNode { Type = "value", Value = 1 }
					}
				},
				new ExpressionNode {
					Type = "gte",
					Members = {
						new ExpressionNode { Type = "neitherSideAtWar" },
						new ExpressionNode { Type = "value", Value = 1 }
					}
				}
			};
			var ctx = new ExpressionContext {
				TargetRulerOrMilitaryOpinion = 40,
				RelationStillExists = 1,
				NeitherSideAtWar = 0
			};

			var results = ActionConditionDebug.EvaluateAll(conditions, ctx);

			Assert.Equal(3, results.Count);
			Assert.Equal("targetRulerOrMilitaryOpinion (40) >= 50", results[0].Label);
			Assert.False(results[0].Passed);
			Assert.Equal("relationStillExists (1) >= 1", results[1].Label);
			Assert.True(results[1].Passed);
			Assert.Equal("neitherSideAtWar (0) >= 1", results[2].Label);
			Assert.False(results[2].Passed);
		}

		[Fact]
		void format_supports_nested_enemy_control_expression() {
			var cond = new ExpressionNode {
				Type = "gt",
				Members = {
					new ExpressionNode {
						Type = "sub",
						Members = {
							new ExpressionNode { Type = "totalCountryControl" },
							new ExpressionNode { Type = "control" }
						}
					},
					new ExpressionNode { Type = "value", Value = 0 }
				}
			};
			var ctx = new ExpressionContext {
				TotalCountryControl = 12,
				Control = 4
			};

			string label = ActionConditionDebug.Format(cond, ctx);

			Assert.Equal("(totalCountryControl (12) - control (4)) > 0", label);
			Assert.Equal(1.0, ExpressionNode.Evaluate(cond, ctx));
		}
	}
}
