using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// ResourceChip atom - an icon plus a value label. Replaces the hand-built
	/// resource-icon/resource-label/resource-row trio in ResourcesView (SharedStyles.uss).
	/// </summary>
	public static class ResourceChipBuilder {
		public struct Elements {
			public VisualElement Chip;
			public VisualElement Icon;
			public Label Label;
		}

		public static Elements Build() {
			var chip = new VisualElement();
			chip.AddToClassList("resource-chip");

			var icon = new VisualElement();
			icon.AddToClassList("resource-chip-icon");
			chip.Add(icon);

			var label = new Label();
			label.AddToClassList("resource-chip-label");
			chip.Add(label);

			return new Elements { Chip = chip, Icon = icon, Label = label };
		}

		public static void Bind(Elements elements, string iconClass, string text) {
			elements.Icon.ClearClassList();
			elements.Icon.AddToClassList("resource-chip-icon");
			if (!string.IsNullOrEmpty(iconClass)) {
				elements.Icon.AddToClassList(iconClass);
			}
			elements.Label.text = text;
		}
	}
}
