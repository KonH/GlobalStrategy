using System.Collections.Generic;

namespace GS.Game.Configs {
	public class EventNotificationSettings {
		public List<EventNotificationEntry> Events { get; set; } = new List<EventNotificationEntry> {
			CreateWarResolvedDefault()
		};

		public static EventNotificationEntry CreateWarResolvedDefault() {
			return new EventNotificationEntry {
				EventType = "war_resolved",
				Pause = true,
				ShowWindow = true,
				WriteActionLog = true,
				PauseCondition = CreateGtControlZero(),
				ShowWindowCondition = CreateGtControlZero(),
				WriteActionLogCondition = null
			};
		}

		public static ExpressionNode CreateGtControlZero() {
			return new ExpressionNode {
				Type = "gt",
				Members = new List<ExpressionNode> {
					new ExpressionNode { Type = "control" },
					new ExpressionNode { Type = "value", Value = 0 }
				}
			};
		}
	}

	public class EventNotificationEntry {
		public string EventType { get; set; } = "";
		public bool Pause { get; set; } = true;
		public bool ShowWindow { get; set; } = true;
		public bool WriteActionLog { get; set; } = true;
		public ExpressionNode? PauseCondition { get; set; }
		public ExpressionNode? ShowWindowCondition { get; set; }
		public ExpressionNode? WriteActionLogCondition { get; set; }
	}
}
