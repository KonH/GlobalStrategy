using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class GoalsWindowView {
		readonly VisualElement _root;
		readonly VisualElement _orgList;
		readonly VisualElement _progressList;
		readonly ILocalization _loc;
		readonly OrgVisualConfig _orgVisualConfig;
		string _selectedOrgId = "";
		LeaderboardState _lastLeaderboard;
		GoalsState _lastGoals;

		public GoalsWindowView(VisualElement root, ILocalization loc, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_orgList = root.Q<VisualElement>("goals-org-list");
			_progressList = root.Q<VisualElement>("goals-progress-list");
		}

		public void ResetToPlayerOrg(string playerOrgId) {
			_selectedOrgId = playerOrgId ?? "";
		}

		public void Refresh(LeaderboardState leaderboard, GoalsState goals) {
			if (_orgList == null || leaderboard == null || goals == null) {
				return;
			}

			_lastLeaderboard = leaderboard;
			_lastGoals = goals;

			_orgList.Clear();
			foreach (var entry in leaderboard.Organizations) {
				_orgList.Add(CreateOrgRow(entry));
			}

			RefreshProgressPanel();
		}

		void SelectOrg(string orgId) {
			if (_selectedOrgId == orgId) {
				return;
			}
			_selectedOrgId = orgId ?? "";
			if (_lastLeaderboard != null && _lastGoals != null) {
				Refresh(_lastLeaderboard, _lastGoals);
			}
		}

		void RefreshProgressPanel() {
			if (_progressList == null || _lastGoals == null) {
				return;
			}

			_progressList.Clear();
			GoalsOrgEntryState selected = null;
			foreach (var org in _lastGoals.Organizations) {
				if (org.OrgId == _selectedOrgId) {
					selected = org;
					break;
				}
			}
			if (selected == null) {
				return;
			}

			foreach (var goal in selected.Goals) {
				_progressList.Add(CreateProgressRow(goal));
			}
		}

		VisualElement CreateOrgRow(LeaderboardEntryState entry) {
			Sprite sprite = _orgVisualConfig?.Find(entry.EntityId)?.flag;
			RankRowBuilder.Elements elements = RankRowBuilder.Build();
			RankRowBuilder.Bind(
				elements, entry.Place, sprite, entry.DisplayName, ScoreFormat.Format(entry.Score),
				highlighted: entry.EntityId == _selectedOrgId);
			string orgId = entry.EntityId;
			elements.Row.OnClick(() => SelectOrg(orgId));
			return elements.Row;
		}

		VisualElement CreateProgressRow(GoalProgressEntryState goal) {
			var row = new VisualElement();
			row.AddToClassList("goals-progress-row");

			var description = new Label(FormatDescription(goal));
			description.AddToClassList("goals-progress-description");
			row.Add(description);

			ProgressBarBuilder.Elements bar = ProgressBarBuilder.Build();
			bar.Track.AddToClassList("goals-progress-track");
			float fraction = goal.Target > 0 ? (float)Math.Min(1.0, goal.Current / goal.Target) : 0f;
			ProgressBarBuilder.Bind(bar, fraction);

			var nm = new Label($"{FormatValue(goal, goal.Current)}/{FormatValue(goal, goal.Target)}");
			nm.AddToClassList("goals-progress-nm");
			bar.Track.Add(nm);

			row.Add(bar.Track);
			return row;
		}

		string FormatDescription(GoalProgressEntryState goal) {
			switch (goal.Kind) {
				case WinConditionHintKind.TotalControl:
					return string.Format(
						GetText("select_org.win_conditions.total_control", "Control {0}% of the World"),
						(goal.ConfigValue * 100).ToString("0", CultureInfo.InvariantCulture));
				case WinConditionHintKind.FullControlCountries:
					return string.Format(
						GetText("select_org.win_conditions.full_control_countries", "Full control of {0}/{1} countries"),
						((int)goal.ConfigValue).ToString(CultureInfo.InvariantCulture),
						goal.AvailableCountryCount.ToString(CultureInfo.InvariantCulture));
				case WinConditionHintKind.ScoreGoal:
					return string.Format(
						GetText("select_org.win_conditions.score_goal", "Reach score {0}"),
						ScoreFormat.Format(goal.ConfigValue));
				case WinConditionHintKind.LastOrgStanding:
					return GetText("goals.last_org_standing", "Destroy every rival organization");
				default:
					return "";
			}
		}

		static string FormatValue(GoalProgressEntryState goal, double value) {
			if (goal.Kind == WinConditionHintKind.ScoreGoal) {
				return ScoreFormat.Format(value);
			}
			return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}
}
