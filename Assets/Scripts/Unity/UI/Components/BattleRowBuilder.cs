using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// BattleRow row component - a single pre-formatted rich-text line, matching how
	/// WarProgressLayoutBinder already formats a battle summary (winner/casualties colours are
	/// baked into the text via rich-text tags, so the row itself only owns layout). Replaces
	/// war-progress-battle-row (WarProgressLayout.uss). ListView makeItem target for phase 4.
	/// </summary>
	public static class BattleRowBuilder {
		public static Label Build() {
			var label = new Label();
			label.AddToClassList("battle-row");
			label.enableRichText = true;
			return label;
		}
	}
}
