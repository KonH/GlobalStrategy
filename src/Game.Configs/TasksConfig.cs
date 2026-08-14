using System.Collections.Generic;

namespace GS.Game.Configs {
	public class TaskRewardEntry {
		public string ResourceId { get; set; } = ResourceDefinitions.Gold;
		public double Amount { get; set; }
	}

	public class TaskDefinition {
		public string TaskId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescKey { get; set; } = "";
		public ExpressionNode? OpenCondition { get; set; }
		public ExpressionNode? CloseCondition { get; set; }
		public List<TaskRewardEntry> Reward { get; set; } = new();
		public List<string> OpenEffectIds { get; set; } = new();
		public List<string> CloseEffectIds { get; set; } = new();
		public bool IsTutorial { get; set; }
		public string HighlightTargetId { get; set; } = "";
	}

	public class TasksConfig {
		public List<TaskDefinition> Tasks { get; set; } = new();

		public TaskDefinition? Find(string taskId) {
			foreach (var task in Tasks) {
				if (task.TaskId == taskId) { return task; }
			}
			return null;
		}
	}
}
