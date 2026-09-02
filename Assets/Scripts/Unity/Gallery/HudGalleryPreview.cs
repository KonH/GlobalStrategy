using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Shared helper for the HUD-panel gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring
	/// phase 7): clones a source UXML (HUD.uxml or FlyText.uxml) and plucks a named sub-element
	/// out of it, the same way DebugGalleryPreview does for Debug.uxml. HUD's own panels are
	/// positioned with `position: absolute` against a full-screen hud-root (see HUD.uss) - for a
	/// small centered ".gallery-stage" that would place the clone off to one side or clipped, so
	/// by default this resets position to Relative and clears the absolute offsets. Pass
	/// `resetToRelative: false` for a block whose stage is ".gallery-stage--surface" (real
	/// 1920x1080 dimensions, position:Relative) - there the cloned root should keep its authored
	/// `position: Absolute; width: 100%; height: 100%;` exactly as-is, since the surface stage now
	/// gives it real dimensions to be absolute/percentage-relative against, matching production.
	/// </summary>
	static class HudGalleryPreview {
		public static VisualElement CloneNamed(VisualTreeAsset sourceUxml, string name, bool resetToRelative = true) {
			if (sourceUxml == null) {
				return null;
			}
			VisualElement root = sourceUxml.CloneTree();
			VisualElement found = root.Q(name);
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
	}
}
