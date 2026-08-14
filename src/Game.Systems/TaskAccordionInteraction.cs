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

			bool currentExpandedStillPresent = currentExpanded != null && Contains(current, currentExpanded);

			// The previously expanded task closed/disappeared: collapse rather than
			// jumping to whatever else happens to be newly active.
			if (currentExpanded != null && !currentExpandedStillPresent) {
				return null;
			}

			foreach (var taskId in current) {
				if (!Contains(previous, taskId)) {
					return taskId;
				}
			}

			return currentExpandedStillPresent ? currentExpanded : null;
		}

		static bool Contains(IReadOnlyList<string> ids, string taskId) {
			foreach (var id in ids) {
				if (id == taskId) {
					return true;
				}
			}
			return false;
		}
	}
}
