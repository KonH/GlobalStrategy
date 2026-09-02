using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Shared helper for the debug-tools gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring
	/// phase 5): clones Debug.uxml and plucks a named sub-element out of it so a block can preview
	/// just one menu without re-authoring its markup by hand.
	/// </summary>
	static class DebugGalleryPreview {
		public static VisualElement CloneRoot(VisualTreeAsset debugUxml) {
			if (debugUxml == null) {
				return null;
			}
			return debugUxml.CloneTree();
		}

		/// <summary>Finds `name` in a fresh clone of debugUxml, forces it visible, and detaches it
		/// so any hidden ancestor in the source tree no longer matters. `debug-panel` and
		/// `top-center` are authored `position: Absolute` with left/right/top/bottom offsets
		/// against a full-screen root (Debug.uss) - pass `resetToRelative: false` for those so they
		/// keep their authored absolute positioning when placed in a ".gallery-stage--surface"
		/// (real 1920x1080 dimensions); the default (true) is for the small, non-absolute
		/// sub-menus that fit the small centered ".gallery-stage" box as-is.</summary>
		public static VisualElement CloneNamed(VisualTreeAsset debugUxml, string name, bool resetToRelative = true) {
			VisualElement root = CloneRoot(debugUxml);
			VisualElement found = root?.Q(name);
			if (found == null) {
				return null;
			}
			found.RemoveFromHierarchy();
			found.style.display = DisplayStyle.Flex;
			if (resetToRelative) {
				found.style.position = Position.Relative;
				found.style.left = StyleKeyword.Null;
				found.style.right = StyleKeyword.Null;
				found.style.top = StyleKeyword.Null;
				found.style.bottom = StyleKeyword.Null;
			}
			return found;
		}

		public static Button CreateSampleButton(string text) {
			var button = new Button { text = text };
			button.AddToClassList("gs-btn");
			button.AddToClassList("gs-btn--small");
			button.AddToClassList("debug-panel-button");
			return button;
		}
	}
}
