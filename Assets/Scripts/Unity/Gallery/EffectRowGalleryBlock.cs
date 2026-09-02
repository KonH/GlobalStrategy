using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews both EffectRow shapes: the single-line list row and the name/value tooltip row.</summary>
	public class EffectRowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Text row", "Name/value row" };
		static readonly List<string> _states = new List<string> { "Positive", "Negative" };

		readonly ILocalization _loc;

		public override string Id => "effect-row";
		public override string Title => "Row: EffectRow";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public EffectRowGalleryBlock(ILocalization loc) {
			_loc = loc;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			bool positive = stateIndex == 0;
			if (instanceId == "Text row") {
				var label = EffectRowBuilder.BuildTextRow();
				label.text = positive ? "Battle result: +5" : "Battle result: -5";
				stage.Add(label);
			} else {
				EffectRowBuilder.NameValueElements row = EffectRowBuilder.BuildNameValueRow();
				EffectRowBuilder.Bind(row, "Gold", positive ? "+50" : "-50", positive);
				stage.Add(row.Row);
			}
		}
	}
}
