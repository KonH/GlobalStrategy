using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the debug panel's "Selected province" sub-menu (Docs/Specs/26_08_28_16_ui-refactoring phase 5).</summary>
	public class DebugProvinceMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Selected province" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-province-menu";
		public override string Title => "Debug: Province sub-menu";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public DebugProvinceMenuGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement menu = DebugGalleryPreview.CloneNamed(_debugUxml, "selected-province-debug-menu");
			if (menu == null) {
				return;
			}
			VisualElement container = menu.Q("province-debug-container");
			if (container != null) {
				var dropdown = new DropdownField { choices = new List<string> { "France", "Germany" }, index = 0 };
				dropdown.AddToClassList("debug-panel-button");
				container.Add(dropdown);
				container.Add(DebugGalleryPreview.CreateSampleButton("Change owner"));
				container.Add(DebugGalleryPreview.CreateSampleButton("Change occupation"));
				container.Add(DebugGalleryPreview.CreateSampleButton("Reset occupation"));
			}
			stage.Add(menu);
		}
	}
}
