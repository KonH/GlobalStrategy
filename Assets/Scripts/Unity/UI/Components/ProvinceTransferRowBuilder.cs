using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// ProvinceTransferRow row component - old flag, arrow, new flag, province name. Replaces
	/// war-result-province-row (WarResultWindow.uss).
	/// </summary>
	public static class ProvinceTransferRowBuilder {
		public struct Elements {
			public VisualElement Row;
			public VisualElement OldFlag;
			public Label Arrow;
			public VisualElement NewFlag;
			public Label Name;
		}

		public static Elements Build() {
			var row = new VisualElement();
			row.AddToClassList("province-transfer-row");

			var oldFlag = FlagBadgeBuilder.Build("war-result-province-flag");
			row.Add(oldFlag);

			var arrow = new Label("->");
			arrow.AddToClassList("gs-content");
			arrow.AddToClassList("province-transfer-row-arrow");
			row.Add(arrow);

			var newFlag = FlagBadgeBuilder.Build("war-result-province-flag");
			row.Add(newFlag);

			var name = new Label();
			name.AddToClassList("gs-content");
			name.AddToClassList("province-transfer-row-name");
			row.Add(name);

			return new Elements { Row = row, OldFlag = oldFlag, Arrow = arrow, NewFlag = newFlag, Name = name };
		}

		public static void Bind(Elements elements, Sprite oldFlag, Sprite newFlag, string provinceName) {
			FlagBadgeBuilder.Bind(elements.OldFlag, oldFlag);
			FlagBadgeBuilder.Bind(elements.NewFlag, newFlag);
			elements.Name.text = provinceName;
		}
	}
}
