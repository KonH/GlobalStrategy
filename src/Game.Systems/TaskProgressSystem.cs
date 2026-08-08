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
			IReadOnlyDictionary<string, double>? triggers = null) {
			if (tasksConfig.Tasks == null || tasksConfig.Tasks.Count == 0) { return; }

			string? playerOrgId = FindPlayerOrgId(world);
			if (playerOrgId == null) { return; }

			var activeByTaskId = new Dictionary<string, int>(StringComparer.Ordinal);
			var completedTaskIds = new HashSet<string>(StringComparer.Ordinal);
			CollectTaskMembership(world, activeByTaskId, completedTaskIds);

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
