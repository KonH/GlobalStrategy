using System.Collections.Generic;

namespace GS.Game.Systems {
	public static class TaskAccordionInteraction {
		public static string? ApplyHeaderClick(string? expandedTaskId, string clickedTaskId) {
			if (expandedTaskId != null) {
				return null;
			}
			return clickedTaskId;
		}

		public static string? SelectInitialExpandedTask(
			IReadOnlyList<string> previous,
			IReadOnlyList<string> current,
			string? currentExpanded) {
			previous ??= System.Array.Empty<string>();
			current ??= System.Array.Empty<string>();

			foreach (var taskId in current) {
				bool wasPresent = false;
				foreach (var prior in previous) {
					if (prior == taskId) {
						wasPresent = true;
						break;
					}
				}
				if (!wasPresent) {
					return taskId;
				}
			}

			if (currentExpanded == null) {
				return null;
			}

			foreach (var taskId in current) {
				if (taskId == currentExpanded) {
					return currentExpanded;
				}
			}
			return null;
		}
	}
}
