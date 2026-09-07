using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// PanelRenderer.rootVisualElement is internal. Binding MonoBehaviours should keep a
	/// RegisterUIReloadCallback subscription and use the root the callback supplies.
	/// This helper is the fallback for call sites that need the current root at query time
	/// (E2E, animators) without owning a long-lived callback.
	/// </summary>
	public static class PanelRendererRoot {
		static readonly PropertyInfo RootProperty = typeof(PanelRenderer).GetProperty(
			"rootVisualElement",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		public static VisualElement Get(PanelRenderer renderer) {
			if (renderer == null) {
				return null;
			}

			VisualElement captured = null;
			void Capture(PanelRenderer _, VisualElement root) {
				captured = root;
			}
			renderer.RegisterUIReloadCallback(Capture);
			renderer.UnregisterUIReloadCallback(Capture);
			if (captured != null) {
				return captured;
			}

			return RootProperty?.GetValue(renderer) as VisualElement;
		}
	}
}
