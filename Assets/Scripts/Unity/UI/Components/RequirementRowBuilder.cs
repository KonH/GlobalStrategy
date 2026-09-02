using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// RequirementRow row component - a single localized condition line, passed or failed.
	/// Replaces ActionConditionText's output styling plus debug-condition-label (HUD.uss), used
	/// by CountryActionsView's action-card requirements and DebugCardAvailabilityView.
	/// </summary>
	public static class RequirementRowBuilder {
		public static Label Build() {
			var label = new Label();
			label.AddToClassList("requirement-row");
			return label;
		}

		/// <summary>Neutral/muted line with no passed/failed state (a section header, e.g.).</summary>
		public static void BindMuted(Label label, string text) {
			label.text = text;
			label.RemoveFromClassList("requirement-row--passed");
			label.RemoveFromClassList("requirement-row--failed");
		}

		public static void Bind(Label label, string text, bool passed) {
			label.text = text;
			label.EnableInClassList("requirement-row--passed", passed);
			label.EnableInClassList("requirement-row--failed", !passed);
		}
	}
}
