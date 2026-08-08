namespace GS.Game.Systems {
	public static class TaskAccordionInteraction {
		public static string? ApplyHeaderClick(string? expandedTaskId, string clickedTaskId) {
			if (expandedTaskId != null) {
				return null;
			}
			return clickedTaskId;
		}
	}
}
