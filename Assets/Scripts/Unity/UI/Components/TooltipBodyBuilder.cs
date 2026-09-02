using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// TooltipBody composite - the shared shapes every hand-built tooltip content builder in this
	/// project repeats: a bold header line, an italic description line, a single tinted text line,
	/// and a name+description effect row. Centralizes the tooltip-header / tooltip-description /
	/// tooltip-effect-* classes (SharedStyles.uss) that CharactersView, CountryInfoView,
	/// OrgCharactersView, ResourcesView, WarIconsView and WarProgressLayoutBinder all used to spell
	/// out by hand (new Label + three AddToClassList calls) every time they needed a tooltip row.
	/// </summary>
	public static class TooltipBodyBuilder {
		public enum LineTone {
			Neutral,
			Positive,
			Negative,
			Hint
		}

		public static VisualElement NewRoot() => new VisualElement();

		public static Label AddHeader(VisualElement root, string text) {
			var header = new Label(text);
			header.AddToClassList("tooltip-header");
			root.Add(header);
			return header;
		}

		public static Label AddDescription(VisualElement root, string text) {
			var description = new Label(text);
			description.AddToClassList("tooltip-description");
			root.Add(description);
			return description;
		}

		/// <summary>A single pre-formatted text line, e.g. "+12.0/month" or "Progress: 40".</summary>
		public static Label AddLine(VisualElement root, string text, LineTone tone = LineTone.Neutral, bool innerTrigger = false) {
			var label = new Label(text);
			label.AddToClassList("tooltip-effect-name");
			ApplyTone(label, tone);
			if (innerTrigger) {
				label.AddToClassList("tooltip-inner-trigger");
			}
			root.Add(label);
			return label;
		}

		/// <summary>A tinted name line plus an optional description line underneath, both wrapped in tooltip-effect-row.</summary>
		public static VisualElement AddEffectRow(VisualElement root, string text, string description, LineTone tone = LineTone.Neutral) {
			var row = new VisualElement();
			row.AddToClassList("tooltip-effect-row");

			var nameLabel = new Label(text);
			nameLabel.AddToClassList("tooltip-effect-name");
			ApplyTone(nameLabel, tone);
			row.Add(nameLabel);

			if (!string.IsNullOrEmpty(description)) {
				var descLabel = new Label(description);
				descLabel.AddToClassList("tooltip-description");
				row.Add(descLabel);
			}

			root.Add(row);
			return row;
		}

		static void ApplyTone(Label label, LineTone tone) {
			switch (tone) {
				case LineTone.Positive:
					label.AddToClassList("tooltip-effect-positive");
					break;
				case LineTone.Negative:
					label.AddToClassList("tooltip-effect-negative");
					break;
				case LineTone.Hint:
					label.AddToClassList("tooltip-effect-hint");
					break;
			}
		}
	}
}
