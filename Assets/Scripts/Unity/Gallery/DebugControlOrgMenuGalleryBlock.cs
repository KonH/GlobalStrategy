using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the debug panel's per-country control-org list and adjust cheat (Docs/Specs/26_08_28_16_ui-refactoring phase 5).</summary>
	public class DebugControlOrgMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Control org" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-control-org-menu";
		public override string Title => "Debug: Control-org sub-menu";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public DebugControlOrgMenuGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement menu = DebugGalleryPreview.CloneNamed(_debugUxml, "selected-country-debug-menu");
			if (menu == null) {
				return;
			}
			VisualElement list = menu.Q("control-org-debug-list");
			if (list != null) {
				foreach (var (name, control) in new[] { ("Sample Org A", 65), ("Sample Org B", 35) }) {
					var label = new Label($"{name}: {control}");
					label.AddToClassList("gs-label");
					label.AddToClassList("debug-panel-button");
					list.Add(label);
				}
			}
			VisualElement container = menu.Q("control-org-debug-container");
			if (container != null) {
				var dropdown = new DropdownField { choices = new List<string> { "Sample Org A", "Sample Org B" }, index = 0 };
				dropdown.AddToClassList("debug-panel-button");
				container.Add(dropdown);
				var row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				row.Add(DebugGalleryPreview.CreateSampleButton("Control+10"));
				row.Add(DebugGalleryPreview.CreateSampleButton("Control-10"));
				container.Add(row);
			}
			stage.Add(menu);
		}
	}
}
