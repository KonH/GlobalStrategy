using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the whole debug panel (Docs/Specs/26_08_28_16_ui-refactoring phase 5),
	/// with its top-level menus forced open so the overall structure is visible at a glance.</summary>
	public class DebugPanelGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Debug panel" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-panel";
		public override string Title => "Debug: Panel";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public DebugPanelGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement panel = DebugGalleryPreview.CloneNamed(_debugUxml, "debug-panel", resetToRelative: false);
			if (panel == null) {
				return;
			}
			foreach (string menuName in new[] {
				"selected-country-debug-menu", "my-org-debug-menu", "selected-org-debug-menu",
				"selected-province-debug-menu", "selected-country-characters", "selected-country-relations",
				"my-org-characters", "my-org-deck", "my-org-hand", "selected-org-deck", "selected-org-hand" }) {
				VisualElement menu = panel.Q(menuName);
				if (menu != null) {
					menu.style.display = DisplayStyle.Flex;
				}
			}
			stage.Add(panel);
		}
	}
}
