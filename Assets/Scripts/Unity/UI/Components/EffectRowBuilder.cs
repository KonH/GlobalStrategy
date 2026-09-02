using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// EffectRow row component, in its two existing shapes: a single pre-formatted rich-text line
	/// (WarProgressLayoutBinder's grouped effect list, war-progress-effect-row) and a name/value
	/// pair with a positive/negative variant (the hand-built tooltip effect row every tooltip
	/// builds today, tooltip-effect-row in SharedStyles.uss). Both replace their old class with
	/// "effect-row" family classes in Components.uss.
	/// </summary>
	public static class EffectRowBuilder {
		public struct NameValueElements {
			public VisualElement Row;
			public Label Name;
			public Label Value;
		}

		/// <summary>The single-line, pre-formatted-rich-text shape used by war/battle effect lists.</summary>
		public static Label BuildTextRow() {
			var label = new Label();
			label.AddToClassList("effect-row");
			label.enableRichText = true;
			return label;
		}

		/// <summary>The name+value shape every tooltip builds for a single effect line.</summary>
		public static NameValueElements BuildNameValueRow() {
			var row = new VisualElement();
			row.AddToClassList("effect-row");

			var name = new Label();
			name.AddToClassList("effect-row-label");
			row.Add(name);

			var value = new Label();
			value.AddToClassList("effect-row-value");
			row.Add(value);

			return new NameValueElements { Row = row, Name = name, Value = value };
		}

		public static void Bind(NameValueElements elements, string name, string value, bool positive) {
			elements.Name.text = name;
			elements.Value.text = value;
			elements.Row.EnableInClassList("effect-row--positive", positive);
			elements.Row.EnableInClassList("effect-row--negative", !positive);
		}
	}
}
