using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the TooltipBody composite's header/description/line/effect-row shapes together.</summary>
	public class TooltipBodyGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Sample tooltip" };
		static readonly List<string> _states = new List<string> { "Full" };

		public override string Id => "tooltip-body";
		public override string Title => "Composite: TooltipBody";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			var root = TooltipBodyBuilder.NewRoot();
			root.AddToClassList("tooltip-overlay");
			root.style.position = Position.Relative;

			TooltipBodyBuilder.AddHeader(root, "Gold");
			TooltipBodyBuilder.AddDescription(root, "The kingdom's treasury.");
			TooltipBodyBuilder.AddLine(root, "+12.0/month", TooltipBodyBuilder.LineTone.Positive, innerTrigger: true);
			TooltipBodyBuilder.AddLine(root, "-3.5/month", TooltipBodyBuilder.LineTone.Negative, innerTrigger: true);
			TooltipBodyBuilder.AddLine(root, "Progress: 40", TooltipBodyBuilder.LineTone.Neutral);
			TooltipBodyBuilder.AddEffectRow(root, "Base income: +4.0/month", "From the country's tax base.", TooltipBodyBuilder.LineTone.Positive);

			stage.Add(root);
		}
	}
}
