using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class EndGameWindowView {
		readonly VisualElement _root;
		readonly Label _header;
		readonly ScrollView _leaderboardList;
		readonly Label _leaderboardEmpty;
		readonly ScrollView _comparisonList;
		readonly ILocalization _loc;
		readonly OrgVisualConfig _orgVisualConfig;

		public EndGameWindowView(VisualElement root, ILocalization loc, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_header = root.Q<Label>("end-game-header");
			_leaderboardList = root.Q<ScrollView>("end-game-leaderboard-list");
			_leaderboardEmpty = root.Q<Label>("end-game-leaderboard-empty");
			_comparisonList = root.Q<ScrollView>("end-game-comparison-list");
		}

		public void Refresh(
			GameCompletionState completion, LeaderboardState leaderboard, PlayerOrganizationState player,
			IReadOnlyList<EndGameComparisonEntry> comparisons) {
			if (_header != null) {
				string key = completion.Result == GameResult.Win ? "end_game.result.win" : "end_game.result.lose";
				_header.text = string.Format(_loc.Get(key), player.DisplayName);
			}

			RefreshLeaderboard(leaderboard);
			RefreshComparison(comparisons, player, leaderboard);
		}

		void RefreshLeaderboard(LeaderboardState leaderboard) {
			if (_leaderboardList == null) {
				return;
			}
			_leaderboardList.Clear();
			foreach (var entry in leaderboard.Organizations) {
				_leaderboardList.Add(CreateLeaderboardRow(entry));
			}
			if (_leaderboardEmpty != null) {
				_leaderboardEmpty.style.display = leaderboard.Organizations.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				_leaderboardEmpty.text = _loc.Get("end_game.leaderboard.empty");
			}
		}

		VisualElement CreateLeaderboardRow(LeaderboardEntryState entry) {
			var row = new VisualElement();
			row.AddToClassList("leaderboard-row");

			var place = new Label(entry.Place.ToString(CultureInfo.InvariantCulture));
			place.AddToClassList("leaderboard-row-place");
			row.Add(place);

			var flag = new VisualElement();
			flag.AddToClassList("leaderboard-row-flag");
			Sprite sprite = _orgVisualConfig?.Find(entry.EntityId)?.flag;
			if (sprite != null) {
				flag.style.backgroundImage = new StyleBackground(sprite);
				flag.style.display = DisplayStyle.Flex;
			} else {
				flag.style.display = DisplayStyle.None;
			}
			row.Add(flag);

			var name = new Label(entry.DisplayName);
			name.AddToClassList("leaderboard-row-name");
			row.Add(name);

			var score = new Label(ScoreFormat.Format(entry.Score));
			score.AddToClassList("leaderboard-row-score");
			row.Add(score);

			return row;
		}

		void RefreshComparison(IReadOnlyList<EndGameComparisonEntry> comparisons, PlayerOrganizationState player, LeaderboardState leaderboard) {
			if (_comparisonList == null) {
				return;
			}
			double playerScore = 0;
			foreach (var entry in leaderboard.Organizations) {
				if (entry.EntityId == player.OrgId) {
					playerScore = entry.Score;
					break;
				}
			}
			_comparisonList.Clear();
			var rows = EndGameComparisonProjector.Build(comparisons, player.OrgId, player.DisplayName, playerScore);
			foreach (var row in rows) {
				_comparisonList.Add(CreateComparisonRow(row));
			}
		}

		VisualElement CreateComparisonRow(EndGameComparisonRowState row) {
			var element = new VisualElement();
			element.AddToClassList("leaderboard-row");

			var place = new Label(row.Place.ToString(CultureInfo.InvariantCulture));
			place.AddToClassList("leaderboard-row-place");
			element.Add(place);

			var flag = new VisualElement();
			flag.AddToClassList("leaderboard-row-flag");
			Sprite sprite = row.IsPlayer ? _orgVisualConfig?.Find(row.ComparisonElementId)?.flag : null;
			if (sprite != null) {
				flag.style.backgroundImage = new StyleBackground(sprite);
				flag.style.display = DisplayStyle.Flex;
			} else {
				flag.style.display = DisplayStyle.None;
			}
			element.Add(flag);

			var name = new Label(GetComparisonDisplayName(row));
			name.AddToClassList("leaderboard-row-name");
			element.Add(name);

			var score = new Label(ScoreFormat.Format(row.Score));
			score.AddToClassList("leaderboard-row-score");
			element.Add(score);

			return element;
		}

		string GetComparisonDisplayName(EndGameComparisonRowState row) {
			if (row.IsPlayer) {
				return row.DisplayName;
			}
			string key = $"end_game.comparison.{row.ComparisonElementId}";
			string localized = _loc?.Get(key) ?? "";
			return !string.IsNullOrEmpty(localized) && localized != key ? localized : row.ComparisonElementId;
		}
	}
}
