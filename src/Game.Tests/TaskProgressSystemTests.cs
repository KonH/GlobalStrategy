using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class TaskProgressSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 15, 12, 0, 0);
		const string OrgId = "org_player";
		const string GrantEffectId = "task_grant_gold";

		static EffectConfig BuildEffectConfig() {
			return new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new OrgResourceGrantEffectParams {
						EffectId = GrantEffectId,
						EffectType = "OrgResourceGrant",
						ResourceId = ResourceDefinitions.Gold,
						Amount = 15
					},
					new CountryResourceModifierEffectParams {
						EffectId = "country_mod",
						EffectType = "CountryResourceModifier",
						ResourceId = ResourceDefinitions.TroopsDamageBonusPercent,
						InitialValue = 5,
						DecayPerMonth = 1
					}
				}
			};
		}

		static void AddPlayerOrg(World world, string orgId) {
			int entity = world.Create();
			world.Add(entity, new Organization { OrganizationId = orgId });
			world.Add(entity, new Player());
		}

		static int AddOrgGold(World world, ResourceQuery resources, string orgId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(orgId, OwnerType.Org));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.Gold, Value = value });
			resources.Rebuild(world);
			return entity;
		}

		static void Run(
			World world,
			TasksConfig tasks,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, double>? triggers = null,
			ITutorialProgressSink? progressSink = null,
			bool forceCompleteActiveTutorials = false) {
			TaskProgressSystem.Update(
				world,
				tasks,
				BuildEffectConfig(),
				CurrentTime,
				new Random(1),
				new GameSettings(),
				new ProvinceTopology(new ProvinceConfig()),
				new Dictionary<string, (double Lon, double Lat)>(),
				100,
				resources,
				relations,
				hqCountryByOrgId: new Dictionary<string, string> { [OrgId] = "hq" },
				triggers: triggers,
				progressSink: progressSink,
				forceCompleteActiveTutorials: forceCompleteActiveTutorials);
		}

		static bool HasActive(World world, string taskId) {
			int[] required = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					if (ids[i].Value == taskId) { return true; }
				}
			}
			return false;
		}

		static bool HasCompleted(World world, string taskId) {
			int[] required = { TypeId<TaskId>.Value, TypeId<TaskCompleted>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					if (ids[i].Value == taskId) { return true; }
				}
			}
			return false;
		}

		static int CountActive(World world) {
			int count = 0;
			int[] required = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				count += arch.Count;
			}
			return count;
		}

		[Fact]
		void opens_when_open_condition_true() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Assert.True(HasActive(world, "t1"));
			Assert.False(HasCompleted(world, "t1"));
		}

		[Fact]
		void does_not_reopen_active_or_completed() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Run(world, tasks, _resources, _relations);
			Assert.Equal(1, CountActive(world));

			var toComplete = new List<int>();
			int[] required = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				for (int i = 0; i < arch.Count; i++) {
					toComplete.Add(arch.Entities[i]);
				}
			}
			foreach (int entity in toComplete) {
				world.Remove<TaskActive>(entity);
				world.Add(entity, new TaskCompleted());
			}

			Run(world, tasks, _resources, _relations);
			Assert.Equal(0, CountActive(world));
			Assert.True(HasCompleted(world, "t1"));
		}

		[Fact]
		void close_marks_completed_and_grants_reward_and_effects() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			int gold = AddOrgGold(world, _resources, OrgId, 10);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode {
							Type = "triggerCondition",
							TriggerId = "done"
						},
						CloseEffectIds = new List<string> { GrantEffectId },
						Reward = new List<TaskRewardEntry> {
							new TaskRewardEntry { ResourceId = ResourceDefinitions.Gold, Amount = 7 }
						}
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Assert.True(HasActive(world, "t1"));

			Run(world, tasks, _resources, _relations, triggers: new Dictionary<string, double> { ["done"] = 1 });
			Assert.False(HasActive(world, "t1"));
			Assert.True(HasCompleted(world, "t1"));
			Assert.Equal(10 + 15 + 7, world.Get<Resource>(gold).Value);

			var changes = new List<ResourceChange>();
			int[] changeRequired = { TypeId<ResourceChange>.Value };
			foreach (var arch in world.GetMatchingArchetypes(changeRequired, null)) {
				ResourceChange[] column = arch.GetColumn<ResourceChange>();
				for (int i = 0; i < arch.Count; i++) {
					changes.Add(column[i]);
				}
			}
			Assert.Contains(changes, c => c.OwnerId == OrgId && c.Amount == 15);
			Assert.Contains(changes, c => c.OwnerId == OrgId && c.Amount == 7 && c.EffectId.StartsWith("task_reward_t1_"));
		}

		[Fact]
		void supports_multiple_concurrent_active_tasks() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "a",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					},
					new TaskDefinition {
						TaskId = "b",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Assert.Equal(2, CountActive(world));
			Assert.True(HasActive(world, "a"));
			Assert.True(HasActive(world, "b"));
		}

		[Fact]
		void close_before_open_prevents_same_tick_activate_and_complete() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 1 }
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Assert.True(HasActive(world, "t1"));
			Assert.False(HasCompleted(world, "t1"));
		}

		[Fact]
		void country_targeted_effect_with_empty_country_is_noop() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 },
						OpenEffectIds = new List<string> { "country_mod" }
					}
				}
			};

			var exception = Record.Exception(() => Run(world, tasks, _resources, _relations));
			Assert.Null(exception);
			Assert.True(HasActive(world, "t1"));
		}

		[Fact]
		void open_applies_open_effects_once() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			int gold = AddOrgGold(world, _resources, OrgId, 5);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t1",
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 },
						OpenEffectIds = new List<string> { GrantEffectId }
					}
				}
			};

			Run(world, tasks, _resources, _relations);
			Assert.Equal(20, world.Get<Resource>(gold).Value);
			Run(world, tasks, _resources, _relations);
			Assert.Equal(20, world.Get<Resource>(gold).Value);
		}

		static ExpressionNode TutorialOpen(string? previousCompletedId = null) {
			var members = new List<ExpressionNode> {
				new ExpressionNode { Type = "triggerCondition", TriggerId = "tutorialsEnabled" },
				new ExpressionNode {
					Type = "eq",
					Members = new List<ExpressionNode> {
						new ExpressionNode { Type = "triggerCondition", TriggerId = "tutorialTaskActive" },
						new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};
			if (!string.IsNullOrEmpty(previousCompletedId)) {
				members.Add(new ExpressionNode {
					Type = "triggerCondition",
					TriggerId = "taskCompleted:" + previousCompletedId
				});
			}
			return new ExpressionNode { Type = "mul", Members = members };
		}

		static int AddGameTime(World world, bool paused) {
			int entity = world.Create();
			world.Add(entity, new GameTime {
				CurrentTime = CurrentTime,
				IsPaused = paused,
				MultiplierIndex = 0,
				AccumulatedHours = 0f
			});
			return entity;
		}

		sealed class RecordingSink : ITutorialProgressSink {
			public List<string> Completed { get; } = new List<string>();
			public void MarkCompleted(string taskId) => Completed.Add(taskId);
		}

		[Fact]
		void tutorial_mutual_exclusion_second_does_not_open_while_one_active() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					},
					new TaskDefinition {
						TaskId = "t1",
						IsTutorial = true,
						OpenCondition = TutorialOpen("t0"),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			var triggers = new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			};
			Run(world, tasks, _resources, _relations, triggers);
			Assert.True(HasActive(world, "t0"));

			triggers["tutorialTaskActive"] = 1;
			triggers["taskCompleted:t0"] = 1;
			Run(world, tasks, _resources, _relations, triggers);
			Assert.True(HasActive(world, "t0"));
			Assert.False(HasActive(world, "t1"));
			Assert.Equal(1, CountActive(world));
		}

		[Fact]
		void tutorial_sequencing_opens_next_after_previous_completed() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "triggerCondition", TriggerId = "done" }
					},
					new TaskDefinition {
						TaskId = "t1",
						IsTutorial = true,
						OpenCondition = TutorialOpen("t0"),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.True(HasActive(world, "t0"));

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 1,
				["done"] = 1
			});
			Assert.True(HasCompleted(world, "t0"));
			Assert.False(HasActive(world, "t1"));

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0,
				["taskCompleted:t0"] = 1
			});
			Assert.True(HasActive(world, "t1"));
		}

		[Fact]
		void force_complete_marks_completed_without_rewards_and_notifies_sink() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			int gold = AddOrgGold(world, _resources, OrgId, 10);
			AddGameTime(world, paused: false);
			var sink = new RecordingSink();
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 },
						CloseEffectIds = new List<string> { GrantEffectId },
						Reward = new List<TaskRewardEntry> {
							new TaskRewardEntry { ResourceId = ResourceDefinitions.Gold, Amount = 7 }
						}
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			}, sink);
			Assert.True(HasActive(world, "t0"));
			Assert.Empty(sink.Completed);

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 0,
				["tutorialTaskActive"] = 1
			}, sink, forceCompleteActiveTutorials: true);

			Assert.False(HasActive(world, "t0"));
			Assert.True(HasCompleted(world, "t0"));
			Assert.Equal(new[] { "t0" }, sink.Completed);
			Assert.Equal(10, world.Get<Resource>(gold).Value);
		}

		[Fact]
		void normal_tutorial_close_notifies_sink() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			AddGameTime(world, paused: false);
			var sink = new RecordingSink();
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "triggerCondition", TriggerId = "done" }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			}, sink);
			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 1,
				["done"] = 1
			}, sink);
			Assert.Equal(new[] { "t0" }, sink.Completed);
		}

		[Fact]
		void non_tutorial_unaffected_when_tutorials_disabled() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "gameplay",
						IsTutorial = false,
						OpenCondition = new ExpressionNode { Type = "value", Value = 1 },
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 0
			}, forceCompleteActiveTutorials: true);
			Assert.True(HasActive(world, "gameplay"));
		}

		[Fact]
		void seeding_creates_completed_so_tutorials_do_not_reopen() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			TaskProgressSystem.SeedCompletedTutorials(world, tasks, new[] { "t0" });
			Assert.True(HasCompleted(world, "t0"));

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0,
				["taskCompleted:t0"] = 1
			});
			Assert.False(HasActive(world, "t0"));
			Assert.Equal(0, CountActive(world));
		}

		[Fact]
		void open_while_unpaused_claims_pause_ownership() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.True(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.True(world.Has<TutorialOwnsPause>(timeEntity));
		}

		[Fact]
		void open_while_paused_does_not_claim_ownership() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: true);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.True(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));
		}

		[Fact]
		void complete_with_ownership_unpauses() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "triggerCondition", TriggerId = "done" }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.True(world.Has<TutorialOwnsPause>(timeEntity));

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 1,
				["done"] = 1
			});
			Assert.False(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));
		}

		[Fact]
		void complete_without_ownership_leaves_paused() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: true);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "triggerCondition", TriggerId = "done" }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 1,
				["done"] = 1
			});
			Assert.True(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));
		}

		[Fact]
		void force_complete_with_ownership_clears_pause() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "value", Value = 0 }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Run(world, tasks, _resources, _relations, forceCompleteActiveTutorials: true);
			Assert.False(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));
		}

		[Fact]
		void save_load_mid_owned_pause_still_unpauses_on_complete() {
			var world = new World();
			AddPlayerOrg(world, OrgId);
			AddOrgGold(world, _resources, OrgId, 0);
			int timeEntity = AddGameTime(world, paused: false);
			var tasks = new TasksConfig {
				Tasks = new List<TaskDefinition> {
					new TaskDefinition {
						TaskId = "t0",
						IsTutorial = true,
						OpenCondition = TutorialOpen(),
						CloseCondition = new ExpressionNode { Type = "triggerCondition", TriggerId = "done" }
					}
				}
			};

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 0
			});
			Assert.True(world.Has<TutorialOwnsPause>(timeEntity));

			// Simulate save/load retaining the savable ownership marker + paused clock.
			Assert.True(world.Get<GameTime>(timeEntity).IsPaused);

			Run(world, tasks, _resources, _relations, new Dictionary<string, double> {
				["tutorialsEnabled"] = 1,
				["tutorialTaskActive"] = 1,
				["done"] = 1
			});
			Assert.False(world.Get<GameTime>(timeEntity).IsPaused);
			Assert.False(world.Has<TutorialOwnsPause>(timeEntity));
		}
	}
}
