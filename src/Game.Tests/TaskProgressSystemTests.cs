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
			IReadOnlyDictionary<string, double>? triggers = null) {
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
				triggers: triggers);
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
	}
}
