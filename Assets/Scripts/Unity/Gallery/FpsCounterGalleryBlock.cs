using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>Previews the always-visible DEBUG toggle + FPS counter row (Docs/Specs/26_08_28_16_ui-refactoring phase 5).</summary>
	public class FpsCounterGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Row" };
		static readonly List<string> _states = new List<string> { "FPS hidden", "FPS shown" };

		readonly VisualTreeAsset _debugUxml;

		public override string Id => "debug-fps-counter";
		public override string Title => "Debug: FPS counter";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override bool IsFullSurface => true;

		public FpsCounterGalleryBlock(VisualTreeAsset debugUxml) {
			_debugUxml = debugUxml;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			VisualElement row = DebugGalleryPreview.CloneNamed(_debugUxml, "top-center", resetToRelative: false);
			if (row == null) {
				return;
			}
			var fpsLabel = row.Q<Label>("fps-label");
			if (fpsLabel != null) {
				bool shown = stateIndex == 1;
				fpsLabel.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
				fpsLabel.text = "FPS: 60";
			}
			stage.Add(row);
		}
	}
}
