using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// FlagNameHeader composite - a flag badge followed by a name label, laid out with the
	/// project-wide ".flag-name-row" class (SharedStyles.uss). Several static UXML documents
	/// already compose this shape declaratively (CountryInfo, ProvinceInfo, OrgInfo, ...); this
	/// builder is for the C#-built instances - e.g. a tooltip's per-entry row - that used to
	/// hand-roll the same three elements. Callers add any extra classes to Label/Row themselves
	/// (the text style varies: plain gs-content in one tooltip, tooltip-effect-name elsewhere).
	/// </summary>
	public static class FlagNameHeaderBuilder {
		public struct Elements {
			public VisualElement Row;
			public VisualElement Flag;
			public Label Label;
		}

		public static Elements Build(string flagSizeClass = "entity-flag") {
			var row = new VisualElement();
			row.AddToClassList("flag-name-row");

			var flag = FlagBadgeBuilder.Build(flagSizeClass);
			row.Add(flag);

			var label = new Label();
			row.Add(label);

			return new Elements { Row = row, Flag = flag, Label = label };
		}

		public static void Bind(Elements elements, Sprite flagSprite, string text) {
			FlagBadgeBuilder.Bind(elements.Flag, flagSprite);
			elements.Label.text = text;
		}
	}
}
