using System;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public static class VisualElementClickExtensions {
		public static void OnClick(this VisualElement element, Action handler) {
			element.RegisterCallback<PointerUpEvent>(evt => {
				if (evt.button != 0 || !element.enabledInHierarchy) {
					return;
				}
				if (!element.ContainsPoint(evt.localPosition)) {
					return;
				}
				handler();
			});
		}
	}
}
