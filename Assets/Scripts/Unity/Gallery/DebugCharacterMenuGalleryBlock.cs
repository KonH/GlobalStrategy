using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the debug panel's "Selected country" > Characters sub-menu (Docs/Specs/26_08_28_16_ui-refactoring phase 5).</summary>
	public class DebugCharacterMenuGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Characters" };
		static readonly List<string> _states = new List<string> { "Default" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-character-menu";
		public override string Title => "Debug: Character sub-menu";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public DebugCharacterMenuGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement menu = DebugGalleryPreview.CloneNamed(_debugUxml, "selected-country-characters");
			if (menu == null) {
				return;
			}
			VisualElement container = menu.Q("character-debug-container");
			if (container != null) {
				foreach (string role in new[] { "military_advisor", "diplomat" }) {
					container.Add(DebugGalleryPreview.CreateSampleButton($"Next: {role}"));
					container.Add(DebugGalleryPreview.CreateSampleButton($"Drop: {role}"));
				}
				container.Add(DebugGalleryPreview.CreateSampleButton("Improve Opinion"));
			}
			stage.Add(menu);
		}
	}
}
