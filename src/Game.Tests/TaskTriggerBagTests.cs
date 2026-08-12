using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class TaskTriggerBagTests {
		static TasksConfig BuildConfig() {
			return new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "tutorial_a",
						IsTutorial = true,
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 }
					},
					new TaskDefinition {
						TaskId = "tutorial_b",
						IsTutorial = true,
						OpenCondition = new ExpressionNode {
							Type = "mul",
							Members = new List<ExpressionNode> {
								new ExpressionNode { Type = "triggerCondition", TriggerId = "tutorialsEnabled" },
								new ExpressionNode {
									Type = "eq",
									Members = new List<ExpressionNode> {
										new ExpressionNode { Type = "triggerCondition", TriggerId = "tutorialTaskActive" },
										new ExpressionNode { Type = "value", Value = 0 }
									}
								},
								new ExpressionNode { Type = "triggerCondition", TriggerId = "taskCompleted:tutorial_a" }
							}
						}
					},
					new TaskDefinition {
						TaskId = "gameplay_task",
						IsTutorial = false
					}
				}
			};
		}

		[Fact]
		void merges_presentation_and_sets_preference_flags() {
			var world = new World();
			var bag = TaskTriggerBag.Build(
				world,
				BuildConfig(),
				new Dictionary<string, double> {
					["mapPositionChanged"] = 1,
					["uiOpened:goalsWindow"] = 1
				},
				tutorialsEnabled: true,
				playerOrgId: "org_player",
				drawCommandThisTick: false,
				receiveCardCommandThisTick: false);

			Assert.Equal(1, bag["mapPositionChanged"]);
			Assert.Equal(1, bag["uiOpened:goalsWindow"]);
			Assert.Equal(1, bag["tutorialsEnabled"]);
			Assert.Equal(0, bag["tutorialTaskActive"]);
			Assert.Equal(0, bag["commandTrigger:draw"]);
			Assert.Equal(0, bag["commandTrigger:cardSelected"]);
		}

		[Fact]
		void tutorialTaskActive_and_taskCompleted_from_world() {
			var world = new World();
			int active = world.Create();
			world.Add(active, new TaskId { Value = "tutorial_a" });
			world.Add(active, new TaskActive());
			int completed = world.Create();
			world.Add(completed, new TaskId { Value = "gameplay_task" });
			world.Add(completed, new TaskCompleted());

			var bag = TaskTriggerBag.Build(
				world, BuildConfig(), new Dictionary<string, double>(),
				tutorialsEnabled: false, "org_player", false, false);

			Assert.Equal(0, bag["tutorialsEnabled"]);
			Assert.Equal(1, bag["tutorialTaskActive"]);
			Assert.Equal(1, bag["taskCompleted:gameplay_task"]);
			Assert.False(bag.ContainsKey("taskCompleted:tutorial_a"));
		}

		[Fact]
		void missing_trigger_evaluates_to_zero_in_open_mul() {
			var world = new World();
			var bag = TaskTriggerBag.Build(
				world, BuildConfig(), new Dictionary<string, double>(),
				tutorialsEnabled: true, "org_player", false, false);
			var ctx = new ExpressionContext { Triggers = bag };
			double value = ExpressionNode.Evaluate(BuildConfig().Tasks[1].OpenCondition!, ctx);
			Assert.Equal(0, value);
		}

		[Fact]
		void chrome_clear_id_passes_through_from_presentation() {
			var world = new World();
			var bag = TaskTriggerBag.Build(
				world, BuildConfig(),
				new Dictionary<string, double> { ["uiElementShown:none"] = 1 },
				true, "org", false, false);
			Assert.Equal(1, bag["uiElementShown:none"]);
		}

		[Fact]
		void command_triggers_set_from_this_tick_flags() {
			var world = new World();
			var bag = TaskTriggerBag.Build(
				world, BuildConfig(), new Dictionary<string, double>(),
				true, "org_player", drawCommandThisTick: true, receiveCardCommandThisTick: true);
			Assert.Equal(1, bag["commandTrigger:draw"]);
			Assert.Equal(1, bag["commandTrigger:cardSelected"]);
		}

		[Fact]
		void player_org_draw_helper_ignores_bot_org() {
			var commands = new ReadCommands<DrawCardsCommand>(new[] {
				new DrawCardsCommand { OrgId = "org_bot" }
			});
			Assert.False(TaskTriggerBag.HasPlayerOrgDrawCommand(commands, "org_player"));
			Assert.True(TaskTriggerBag.HasPlayerOrgDrawCommand(
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "org_player" } }),
				"org_player"));
		}

		[Fact]
		void player_org_receive_helper_ignores_bot_org() {
			var commands = new ReadCommands<ReceiveCardCommand>(new[] {
				new ReceiveCardCommand { OrgId = "org_bot", ChoiceIndex = 0 }
			});
			Assert.False(TaskTriggerBag.HasPlayerOrgReceiveCardCommand(commands, "org_player"));
			Assert.True(TaskTriggerBag.HasPlayerOrgReceiveCardCommand(
				new ReadCommands<ReceiveCardCommand>(new[] {
					new ReceiveCardCommand { OrgId = "org_player", ChoiceIndex = 1 }
				}),
				"org_player"));
		}

		[Fact]
		void clear_task_edges_removes_map_and_key_latches() {
			var bag = new Dictionary<string, double> {
				["mapPositionChanged"] = 1,
				["mapZoomChanged"] = 1,
				["keyPressed"] = 1,
				["uiOpened:goalsWindow"] = 1
			};
			TaskTriggerBag.ClearTaskEdges(bag);
			Assert.False(bag.ContainsKey("mapPositionChanged"));
			Assert.False(bag.ContainsKey("mapZoomChanged"));
			Assert.False(bag.ContainsKey("keyPressed"));
			Assert.Equal(1, bag["uiOpened:goalsWindow"]);
		}
	}
}
