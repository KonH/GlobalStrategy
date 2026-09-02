using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// HandContainer composite - the split deck-column + cards-grid layout CountryActionsView
	/// builds around a country's hand (Assets/UI/Overlay/OrgInfo/OrgActions.uss ".hand-container--
	/// split" family, also used by the simpler single-row ".hand-container" OrgActionsView uses).
	/// CountryActionsView/OrgActionsView switch to calling this in phase 7.
	/// </summary>
	public static class HandContainerBuilder {
		public struct SplitElements {
			public VisualElement Root;
			public VisualElement DeckColumn;
			public VisualElement CardsGrid;
		}

		/// <summary>The country-actions shape: a dedicated deck column beside a wrapping cards grid.</summary>
		public static SplitElements BuildSplit() {
			var root = new VisualElement();
			root.AddToClassList("hand-container--split");

			var deckColumn = new VisualElement();
			deckColumn.AddToClassList("hand-deck-column");
			root.Add(deckColumn);

			var cardsGrid = new VisualElement();
			cardsGrid.AddToClassList("hand-cards-grid");
			root.Add(cardsGrid);

			return new SplitElements { Root = root, DeckColumn = deckColumn, CardsGrid = cardsGrid };
		}

		/// <summary>The org-actions shape: one wrapping row holding the deck pile and every card.</summary>
		public static VisualElement BuildRow() {
			var root = new VisualElement();
			root.AddToClassList("hand-container");
			return root;
		}
	}
}
