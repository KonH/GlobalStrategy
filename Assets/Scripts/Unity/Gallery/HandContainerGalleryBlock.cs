using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the HandContainer composite's two shapes: the country-actions split layout and the org-actions single row.</summary>
	public class HandContainerGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Hand" };
		static readonly List<string> _states = new List<string> { "Split (country actions)", "Row (org actions)" };

		public override string Id => "hand-container";
		public override string Title => "Composite: HandContainer";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			if (stateIndex == 1) {
				VisualElement row = HandContainerBuilder.BuildRow();
				for (int i = 0; i < 3; i++) {
					row.Add(BuildPlaceholderCard());
				}
				stage.Add(row);
				return;
			}

			HandContainerBuilder.SplitElements elements = HandContainerBuilder.BuildSplit();
			elements.DeckColumn.Add(BuildPlaceholderCard());
			for (int i = 0; i < 3; i++) {
				elements.CardsGrid.Add(BuildPlaceholderCard());
			}
			stage.Add(elements.Root);
		}

		static VisualElement BuildPlaceholderCard() {
			var wrapper = new VisualElement();
			wrapper.AddToClassList("card-lift-wrapper");
			var card = new VisualElement();
			card.AddToClassList("action-card");
			card.AddToClassList("action-card--available");
			wrapper.Add(card);
			return wrapper;
		}
	}
}
