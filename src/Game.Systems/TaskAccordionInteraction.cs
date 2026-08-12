using System.Collections.Generic;

namespace GS.Game.Systems {
	public static class TaskAccordionInteraction {
		public static string? ApplyHeaderClick(string? expandedTaskId, string clickedTaskId) {
			if (expandedTaskId != null) {
				return null;
			}
			return clickedTaskId;
		}

		public static string? SelectInitialExpandedTutorial(
			IReadOnlyList<(string TaskId, bool IsTutorial)> previous,
			IReadOnlyList<(string TaskId, bool IsTutorial)> current,
			string? currentExpanded) {
			previous ??= System.Array.Empty<(string, bool)>();
			current ??= System.Array.Empty<(string, bool)>();

			foreach (var task in current) {
				if (!task.IsTutorial) { continue; }
				bool wasPresent = false;
				foreach (var prior in previous) {
					if (prior.TaskId == task.TaskId) {
						wasPresent = true;
						break;
					}
				}
				if (!wasPresent) {
					return task.TaskId;
				}
			}

			if (currentExpanded == null) {
				return null;
			}

			foreach (var task in current) {
				if (task.TaskId == currentExpanded) {
					return currentExpanded;
				}
			}
			return null;
		}
	}
}
