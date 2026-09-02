using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the DrawSlot atom (CardDrawView's per-choice placeholder) empty and with a placeholder card sitting over it.</summary>
	public class DrawSlotGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Slot" };
		static readonly List<string> _states = new List<string> { "Empty", "Occupied" };

		public override string Id => "draw-slot";
		public override string Title => "Atom: DrawSlot";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			var host = new VisualElement();
			host.style.position = Position.Relative;
			VisualElement slot = DrawSlotBuilder.Build();
			host.Add(slot);

			if (stateIndex == 1) {
				var card = new VisualElement();
				card.AddToClassList("action-card");
				card.AddToClassList("action-card--available");
				card.style.position = Position.Absolute;
				card.style.left = 0;
				card.style.top = 0;
				host.Add(card);
			}

			stage.Add(host);
		}
	}
}
