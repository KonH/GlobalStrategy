using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class TaskProgressSystem {
		public static void Update(
			World world,
			TasksConfig tasksConfig,
			EffectConfig effectConfig,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			CountryConfig? countryConfig = null,
			IReadOnlyDictionary<string, double>? triggers = null,
			ITutorialProgressSink? progressSink = null,
			bool forceCompleteActiveTutorials = false) {
			if (tasksConfig.Tasks == null || tasksConfig.Tasks.Count == 0) { return; }

			string? playerOrgId = FindPlayerOrgId(world);
			if (playerOrgId == null) { return; }

			var activeByTaskId = new Dictionary<string, int>(StringComparer.Ordinal);
			var completedTaskIds = new HashSet<string>(StringComparer.Ordinal);
			CollectTaskMembership(world, activeByTaskId, completedTaskIds);

			if (forceCompleteActiveTutorials) {
				ForceCompleteActiveTutorials(
					world, tasksConfig, progressSink, activeByTaskId, completedTaskIds);
			}

			ExpressionContext context = TaskConditionContext.Build(
				world, playerOrgId, resources, relations, hqCountryByOrgId, triggers);

			foreach (var task in tasksConfig.Tasks) {
				if (string.IsNullOrEmpty(task.TaskId)) { continue; }
				if (!activeByTaskId.TryGetValue(task.TaskId, out int activeEntity)) { continue; }
				if (task.CloseCondition == null) { continue; }
				if (ExpressionNode.Evaluate(task.CloseCondition, context) < 1.0) { continue; }

				EffectApplicator.ApplyEffectIds(
					world,
					effectConfig,
					task.CloseEffectIds,
					playerOrgId,
					countryId: "",
					currentTime,
					rng,
					settings,
					topology,
					provinceCenters,
					maxControlPool,
					resources,
					countryConfig,
					contextEntity: -1,
					correlationId: task.TaskId,
					targetRole: "");

				if (task.Reward != null) {
					foreach (var reward in task.Reward) {
						if (string.IsNullOrEmpty(reward.ResourceId) || reward.Amount == 0) { continue; }
						EffectApplicator.GrantTaskReward(
							world, resources, playerOrgId, reward.ResourceId, reward.Amount, task.TaskId, currentTime);
					}
				}

				world.Remove<TaskActive>(activeEntity);
				world.Add(activeEntity, new TaskCompleted());
				activeByTaskId.Remove(task.TaskId);
				completedTaskIds.Add(task.TaskId);

				if (task.IsTutorial) {
					progressSink?.MarkCompleted(task.TaskId);
					ReleaseTutorialPauseOwnership(world);
				}
			}

			foreach (var task in tasksConfig.Tasks) {
				if (string.IsNullOrEmpty(task.TaskId)) { continue; }
				if (activeByTaskId.ContainsKey(task.TaskId) || completedTaskIds.Contains(task.TaskId)) { continue; }
				if (task.OpenCondition == null) { continue; }
				if (ExpressionNode.Evaluate(task.OpenCondition, context) < 1.0) { continue; }

				int entity = world.Create();
				world.Add(entity, new TaskId { Value = task.TaskId });
				world.Add(entity, new TaskActive());
				activeByTaskId[task.TaskId] = entity;

				if (task.IsTutorial) {
					ClaimTutorialPauseOwnership(world);
				}

				EffectApplicator.ApplyEffectIds(
					world,
					effectConfig,
					task.OpenEffectIds,
					playerOrgId,
					countryId: "",
					currentTime,
					rng,
					settings,
					topology,
					provinceCenters,
					maxControlPool,
					resources,
					countryConfig,
					contextEntity: -1,
					correlationId: task.TaskId,
					targetRole: "");
			}
		}

		public static void SeedCompletedTutorials(
			World world,
			TasksConfig tasksConfig,
			IEnumerable<string> completedIds) {
			if (tasksConfig.Tasks == null || tasksConfig.Tasks.Count == 0 || completedIds == null) {
				return;
			}

			var alreadyCompleted = new HashSet<string>(StringComparer.Ordinal);
			int[] completedRequired = { TypeId<TaskId>.Value, TypeId<TaskCompleted>.Value };
			foreach (var arch in world.GetMatchingArchetypes(completedRequired, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					alreadyCompleted.Add(ids[i].Value);
				}
			}

			foreach (string taskId in completedIds) {
				if (string.IsNullOrEmpty(taskId) || alreadyCompleted.Contains(taskId)) { continue; }
				TaskDefinition? def = tasksConfig.Find(taskId);
				if (def == null || !def.IsTutorial) { continue; }

				int entity = world.Create();
				world.Add(entity, new TaskId { Value = taskId });
				world.Add(entity, new TaskCompleted());
				alreadyCompleted.Add(taskId);
			}
		}

		public static string? FindActiveTutorialId(World world, TasksConfig tasksConfig) {
			if (tasksConfig.Tasks == null) { return null; }
			int[] required = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					TaskDefinition? def = tasksConfig.Find(ids[i].Value);
					if (def != null && def.IsTutorial) {
						return ids[i].Value;
					}
				}
			}
			return null;
		}

		static void ForceCompleteActiveTutorials(
			World world,
			TasksConfig tasksConfig,
			ITutorialProgressSink? progressSink,
			Dictionary<string, int> activeByTaskId,
			HashSet<string> completedTaskIds) {
			var toForce = new List<(string TaskId, int Entity)>();
			foreach (var pair in activeByTaskId) {
				TaskDefinition? def = tasksConfig.Find(pair.Key);
				if (def != null && def.IsTutorial) {
					toForce.Add((pair.Key, pair.Value));
				}
			}

			if (toForce.Count == 0) { return; }

			foreach (var (taskId, entity) in toForce) {
				world.Remove<TaskActive>(entity);
				world.Add(entity, new TaskCompleted());
				activeByTaskId.Remove(taskId);
				completedTaskIds.Add(taskId);
				progressSink?.MarkCompleted(taskId);
			}

			ReleaseTutorialPauseOwnership(world);
		}

		static void ClaimTutorialPauseOwnership(World world) {
			if (!TryGetGameTimeEntity(world, out int gameTimeEntity)) { return; }
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			if (time.IsPaused) { return; }
			time.IsPaused = true;
			if (!world.Has<TutorialOwnsPause>(gameTimeEntity)) {
				world.Add(gameTimeEntity, new TutorialOwnsPause());
			}
		}

		static void ReleaseTutorialPauseOwnership(World world) {
			if (!TryGetGameTimeEntity(world, out int gameTimeEntity)) { return; }
			if (!world.Has<TutorialOwnsPause>(gameTimeEntity)) { return; }
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			time.IsPaused = false;
			world.Remove<TutorialOwnsPause>(gameTimeEntity);
		}

		static bool TryGetGameTimeEntity(World world, out int entity) {
			int[] required = { TypeId<GameTime>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					entity = arch.Entities[0];
					return true;
				}
			}
			entity = -1;
			return false;
		}

		static string? FindPlayerOrgId(World world) {
			int[] required = { TypeId<Organization>.Value, TypeId<Player>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				if (arch.Count > 0) {
					return orgs[0].OrganizationId;
				}
			}
			return null;
		}

		static void CollectTaskMembership(
			World world,
			Dictionary<string, int> activeByTaskId,
			HashSet<string> completedTaskIds) {
			int[] activeRequired = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(activeRequired, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					activeByTaskId[ids[i].Value] = arch.Entities[i];
				}
			}

			int[] completedRequired = { TypeId<TaskId>.Value, TypeId<TaskCompleted>.Value };
			foreach (var arch in world.GetMatchingArchetypes(completedRequired, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					completedTaskIds.Add(ids[i].Value);
				}
			}
		}
	}
}
