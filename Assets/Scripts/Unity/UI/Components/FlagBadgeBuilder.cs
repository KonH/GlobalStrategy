using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// FlagBadge atom - a single flag/emblem image. Replaces the six ad-hoc "*-flag" classes
	/// (entity-flag, relations-flag, goals-row-flag, leaderboard-row-flag,
	/// war-result-province-flag, war-icon-flag): every call site still adds its own layout-only
	/// size/margin class (kept in its own feature stylesheet, unifying only where RankRow already
	/// unifies goals/leaderboard), while the shared look (picking mode, scale mode) lives once in
	/// Assets/UI/Components/Components.uss under ".flag-badge".
	/// </summary>
	public static class FlagBadgeBuilder {
		public static VisualElement Build(string sizeClass = null) {
			var flag = new VisualElement {
				pickingMode = PickingMode.Ignore
			};
			flag.AddToClassList("flag-badge");
			if (!string.IsNullOrEmpty(sizeClass)) {
				flag.AddToClassList(sizeClass);
			}
			return flag;
		}

		public static void Bind(VisualElement flag, Sprite sprite) {
			if (flag == null) {
				return;
			}
			if (sprite != null) {
				flag.style.backgroundImage = new StyleBackground(sprite);
				flag.style.display = DisplayStyle.Flex;
			} else {
				flag.style.display = DisplayStyle.None;
			}
		}
	}
}
