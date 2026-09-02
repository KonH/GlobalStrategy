using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the debug panel's "Selected country" > Relations sub-menu (Docs/Specs/26_08_28_16_ui-refactoring phase 5).</summary>
	public class DebugRelationMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Relations" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-relation-menu";
		public override string Title => "Debug: Relations sub-menu";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public DebugRelationMenuGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement menu = DebugGalleryPreview.CloneNamed(_debugUxml, "selected-country-relations");
			if (menu == null) {
				return;
			}
			VisualElement container = menu.Q("relation-debug-container");
			if (container != null) {
				var dropdown = new DropdownField { choices = new List<string> { "Germany", "Italy (Friend)" }, index = 0 };
				dropdown.AddToClassList("debug-panel-button");
				container.Add(dropdown);
				container.Add(DebugGalleryPreview.CreateSampleButton("Set friend"));
				container.Add(DebugGalleryPreview.CreateSampleButton("Set rival"));
				container.Add(DebugGalleryPreview.CreateSampleButton("Clear relation"));
			}
			stage.Add(menu);
		}
	}
}
