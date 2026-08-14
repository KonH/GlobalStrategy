using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class TaskTriggerBag {
		public const string TutorialsEnabled = "tutorialsEnabled";
		public const string TutorialTaskActive = "tutorialTaskActive";
		public const string CommandTriggerDraw = "commandTrigger:draw";
		public const string CommandTriggerCardSelected = "commandTrigger:cardSelected";
		public const string MapPositionChanged = "mapPositionChanged";
		public const string MapZoomChanged = "mapZoomChanged";
		public const string KeyPressed = "keyPressed";

		public static Dictionary<string, double> Build(
			World world,
			TasksConfig tasksConfig,
			IReadOnlyDictionary<string, double> presentationTriggers,
			bool tutorialsEnabled,
			string playerOrgId,
			bool drawCommandThisTick,
			bool receiveCardCommandThisTick) {
			var result = new Dictionary<string, double>(StringComparer.Ordinal);

			if (presentationTriggers != null) {
				foreach (var pair in presentationTriggers) {
					result[pair.Key] = pair.Value;
				}
			}

			result[TutorialsEnabled] = tutorialsEnabled ? 1.0 : 0.0;

			bool tutorialTaskActive = false;
			int[] activeRequired = { TypeId<TaskId>.Value, TypeId<TaskActive>.Value };
			foreach (var arch in world.GetMatchingArchetypes(activeRequired, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					TaskDefinition? def = tasksConfig.Find(ids[i].Value);
					if (def != null && def.IsTutorial) {
						tutorialTaskActive = true;
						break;
					}
				}
				if (tutorialTaskActive) { break; }
			}
			result[TutorialTaskActive] = tutorialTaskActive ? 1.0 : 0.0;

			int[] completedRequired = { TypeId<TaskId>.Value, TypeId<TaskCompleted>.Value };
			foreach (var arch in world.GetMatchingArchetypes(completedRequired, null)) {
				TaskId[] ids = arch.GetColumn<TaskId>();
				for (int i = 0; i < arch.Count; i++) {
					string taskId = ids[i].Value;
					if (string.IsNullOrEmpty(taskId)) { continue; }
					result["taskCompleted:" + taskId] = 1.0;
				}
			}

			_ = playerOrgId;
			result[CommandTriggerDraw] = drawCommandThisTick ? 1.0 : 0.0;
			result[CommandTriggerCardSelected] = receiveCardCommandThisTick ? 1.0 : 0.0;
			return result;
		}

		public static bool HasPlayerOrgDrawCommand(
			ReadCommands<DrawCardsCommand> commands,
			string playerOrgId) {
			if (string.IsNullOrEmpty(playerOrgId)) { return false; }
			foreach (var command in commands.AsSpan()) {
				if (command.OrgId == playerOrgId) { return true; }
			}
			return false;
		}

		public static bool HasPlayerOrgReceiveCardCommand(
			ReadCommands<ReceiveCardCommand> commands,
			string playerOrgId) {
			if (string.IsNullOrEmpty(playerOrgId)) { return false; }
			foreach (var command in commands.AsSpan()) {
				if (command.OrgId == playerOrgId) { return true; }
			}
			return false;
		}

		public static void ClearTaskEdges(IDictionary<string, double> presentationTriggers) {
			if (presentationTriggers == null) { return; }
			presentationTriggers.Remove(MapPositionChanged);
			presentationTriggers.Remove(MapZoomChanged);
			presentationTriggers.Remove(KeyPressed);
		}
	}
}
