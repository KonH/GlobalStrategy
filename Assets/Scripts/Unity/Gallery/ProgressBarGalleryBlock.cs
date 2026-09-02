using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the ProgressBar atom used by the goals list and war progress.</summary>
	public class ProgressBarGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Progress" };
		static readonly List<string> _states = new List<string> { "0%", "35%", "70%", "100%" };

		public override string Id => "progress-bar";
		public override string Title => "Atom: ProgressBar";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			float fraction = stateIndex switch { 0 => 0f, 1 => 0.35f, 2 => 0.7f, _ => 1f };
			ProgressBarBuilder.Elements bar = ProgressBarBuilder.Build();
			bar.Track.style.width = 240;
			ProgressBarBuilder.Bind(bar, fraction);
			stage.Add(bar.Track);
		}
	}
}
