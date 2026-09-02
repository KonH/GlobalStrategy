using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// StatChip atom - an icon plus a value label, sized for the character stat row. Replaces the
	/// hand-built char-stat-chip/char-stat-icon pair (SharedStyles.uss) in CharactersView and
	/// OrgCharactersView.
	/// </summary>
	public static class StatChipBuilder {
		public struct Elements {
			public VisualElement Chip;
			public VisualElement Icon;
			public Label Label;
		}

		public static Elements Build() {
			var chip = new VisualElement();
			chip.AddToClassList("stat-chip");
			chip.AddToClassList("char-stat-chip");

			var icon = new VisualElement();
			icon.AddToClassList("stat-chip-icon");
			icon.AddToClassList("char-stat-icon");
			chip.Add(icon);

			var label = new Label();
			label.AddToClassList("stat-chip-label");
			chip.Add(label);

			return new Elements { Chip = chip, Icon = icon, Label = label };
		}

		public static void Bind(Elements elements, string text, params string[] iconExtraClasses) {
			elements.Icon.ClearClassList();
			elements.Icon.AddToClassList("stat-chip-icon");
			elements.Icon.AddToClassList("char-stat-icon");
			if (iconExtraClasses != null) {
				foreach (string extraClass in iconExtraClasses) {
					if (!string.IsNullOrEmpty(extraClass)) {
						elements.Icon.AddToClassList(extraClass);
					}
				}
			}
			elements.Label.text = text;
		}
	}
}
