using System;
using System.Collections;
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
		readonly ListView _leaderboardList;
		readonly Label _leaderboardEmpty;
		readonly ListView _comparisonList;
		readonly ILocalization _loc;
		readonly OrgVisualConfig _orgVisualConfig;
		IReadOnlyList<LeaderboardEntryState> _currentLeaderboardEntries = Array.Empty<LeaderboardEntryState>();
		string _currentPlayerOrgId = "";
		IReadOnlyList<EndGameComparisonRowState> _currentComparisonRows = Array.Empty<EndGameComparisonRowState>();

		public EndGameWindowView(VisualElement root, ILocalization loc, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_header = root.Q<Label>("end-game-header");
			_leaderboardList = root.Q<ListView>("end-game-leaderboard-list");
			_leaderboardEmpty = root.Q<Label>("end-game-leaderboard-empty");
			_comparisonList = root.Q<ListView>("end-game-comparison-list");

			if (_leaderboardList != null) {
				_leaderboardList.makeItem = () => {
					RankRowBuilder.Elements elements = RankRowBuilder.Build();
					elements.Row.userData = elements;
					return elements.Row;
				};
				_leaderboardList.bindItem = (element, index) => BindLeaderboardRow(element, _currentLeaderboardEntries[index]);
			}
			if (_comparisonList != null) {
				_comparisonList.makeItem = () => {
					RankRowBuilder.Elements elements = RankRowBuilder.Build();
					elements.Row.userData = elements;
					return elements.Row;
				};
				_comparisonList.bindItem = (element, index) => BindComparisonRow(element, _currentComparisonRows[index]);
			}
		}

		public void Refresh(
			GameCompletionState completion, LeaderboardState leaderboard, PlayerOrganizationState player,
			IReadOnlyList<EndGameComparisonEntry> comparisons) {
			if (_header != null) {
				_header.text = BuildHeaderText(completion, player, leaderboard);
			}

			RefreshLeaderboard(leaderboard, player.OrgId);
			RefreshComparison(comparisons, player, leaderboard);
		}

		// Three header variants:
		// - Win: the player's own org reached the win condition.
		// - Lose with a declared winner: some other org reached the win condition first.
		// - Lose with no declared winner: the player's org was destroyed while 2+ other
		//   orgs still stood, so GameCompletionSystem.ApplyPlayerDestroyedLoss ends the
		//   game without picking a winner among the survivors (see its test
		//   destroyed_player_fallback_completes_without_a_winner_and_preserves_survivors) -
		//   WinnerOrganizationId is deliberately left empty in that case.
		string BuildHeaderText(GameCompletionState completion, PlayerOrganizationState player, LeaderboardState leaderboard) {
			if (completion.Result == GameResult.Win) {
				return string.Format(_loc.Get("end_game.result.win"), player.DisplayName);
			}
			if (string.IsNullOrEmpty(completion.WinnerOrganizationId)) {
				return _loc.Get("end_game.result.destroyed");
			}
			return string.Format(_loc.Get("end_game.result.lose"), GetWinnerDisplayName(completion.WinnerOrganizationId, leaderboard));
		}

		string GetWinnerDisplayName(string winnerOrganizationId, LeaderboardState leaderboard) {
			foreach (var entry in leaderboard.Organizations) {
				if (entry.EntityId == winnerOrganizationId) {
					return entry.DisplayName;
				}
			}
			// Winner id is known but not present in the leaderboard snapshot (e.g. it hasn't
			// refreshed yet this tick). Show the raw id instead of silently misattributing it.
			Debug.LogWarning(
				$"[EndGameWindowView] winner id '{winnerOrganizationId}' not found in leaderboard " +
				$"({leaderboard.Organizations.Count} entries); showing raw id.");
			return winnerOrganizationId;
		}

		void RefreshLeaderboard(LeaderboardState leaderboard, string playerOrgId) {
			if (_leaderboardList == null) {
				return;
			}
			_currentPlayerOrgId = playerOrgId ?? "";
			_currentLeaderboardEntries = leaderboard.Organizations;
			_leaderboardList.itemsSource = (IList)_currentLeaderboardEntries;
			_leaderboardList.Rebuild();
			if (_leaderboardEmpty != null) {
				_leaderboardEmpty.style.display = _currentLeaderboardEntries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				_leaderboardEmpty.text = _loc.Get("end_game.leaderboard.empty");
			}
		}

		void BindLeaderboardRow(VisualElement element, LeaderboardEntryState entry) {
			Sprite sprite = _orgVisualConfig?.Find(entry.EntityId)?.flag;
			var elements = (RankRowBuilder.Elements)element.userData;
			RankRowBuilder.Bind(elements, entry.Place, sprite, entry.DisplayName, ScoreFormat.Format(entry.Score), highlighted: entry.EntityId == _currentPlayerOrgId);
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
			_currentComparisonRows = EndGameComparisonProjector.Build(comparisons, player.OrgId, player.DisplayName, playerScore);
			_comparisonList.itemsSource = (IList)_currentComparisonRows;
			_comparisonList.Rebuild();
		}

		void BindComparisonRow(VisualElement element, EndGameComparisonRowState row) {
			Sprite sprite = row.IsPlayer ? _orgVisualConfig?.Find(row.ComparisonElementId)?.flag : null;
			var elements = (RankRowBuilder.Elements)element.userData;
			RankRowBuilder.Bind(elements, row.Place, sprite, GetComparisonDisplayName(row), ScoreFormat.Format(row.Score), highlighted: row.IsPlayer);
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
