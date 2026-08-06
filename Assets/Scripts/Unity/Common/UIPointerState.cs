using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.Common {
	public static class UIPointerState {
		public static IPanel RuntimePanel { get; set; }

		public static bool IsPointerOverUI(Vector2 screenPosition) {
			if (RuntimePanel == null) {
				return false;
			}
			// Callers pass Mouse/Touch position, which Unity reports Y-up (0 at the bottom of
			// the screen). RuntimePanelUtils.ScreenToPanel does not flip this on its own — it
			// expects a Y-down coordinate (0 at the top), matching UI Toolkit's panel space.
			// Without this flip, any element in the screen's lower half is missed by Pick().
			Vector2 topDownScreenPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
			Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(RuntimePanel, topDownScreenPosition);
			return RuntimePanel.Pick(panelPosition) != null;
		}
	}
}
