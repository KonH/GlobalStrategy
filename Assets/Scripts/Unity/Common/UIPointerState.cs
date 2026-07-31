using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.Common {
	public static class UIPointerState {
		public static IPanel RuntimePanel { get; set; }

		public static bool IsPointerOverUI(Vector2 screenPosition) {
			if (RuntimePanel == null) {
				return false;
			}
			Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(RuntimePanel, screenPosition);
			return RuntimePanel.Pick(panelPosition) != null;
		}
	}
}
