using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// RankRow row component - place, flag, name and score. Replaces the leaderboard-row
	/// (LeaderboardWindow.uss, EndGameWindow.uss) and goals-row (GoalsWindow.uss) shapes, which
	/// were already visually identical apart from styling - see .rank-row in Components.uss for
	/// the unified look (the accepted small visual change per spec.md's RankRow exception).
	/// This is the ListView makeItem target for phase 4's leaderboard/goals/end-game lists.
	/// </summary>
	public static class RankRowBuilder {
		public struct Elements {
			public VisualElement Row;
			public Label Place;
			public VisualElement Flag;
			public Label Name;
			public Label Score;
		}

		public static Elements Build() {
			var row = new VisualElement();
			row.AddToClassList("rank-row");

			var place = new Label();
			place.AddToClassList("rank-row-place");
			row.Add(place);

			var flag = FlagBadgeBuilder.Build("rank-row-flag");
			row.Add(flag);

			var name = new Label();
			name.AddToClassList("rank-row-name");
			row.Add(name);

			var score = new Label();
			score.AddToClassList("rank-row-score");
			row.Add(score);

			return new Elements { Row = row, Place = place, Flag = flag, Name = name, Score = score };
		}

		public static void Bind(Elements elements, int place, Sprite flag, string name, string score, bool highlighted = false) {
			elements.Place.text = place.ToString(CultureInfo.InvariantCulture);
			FlagBadgeBuilder.Bind(elements.Flag, flag);
			elements.Name.text = name;
			elements.Score.text = score;
			elements.Row.EnableInClassList("rank-row--highlighted", highlighted);
		}
	}
}
