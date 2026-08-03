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
		readonly ScrollView _orgList;
		readonly ScrollView _progressList;
		readonly ILocalization _loc;
		readonly OrgVisualConfig _orgVisualConfig;
		string _selectedOrgId = "";
		LeaderboardState _lastLeaderboard;
		GoalsState _lastGoals;

		public GoalsWindowView(VisualElement root, ILocalization loc, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_orgList = root.Q<ScrollView>("goals-org-list");
			_progressList = root.Q<ScrollView>("goals-progress-list");
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

			Vector2 scrollOffset = _orgList.scrollOffset;
			_orgList.Clear();
			foreach (var entry in leaderboard.Organizations) {
				_orgList.Add(CreateOrgRow(entry));
			}
			_orgList.schedule.Execute(() => _orgList.scrollOffset = scrollOffset);

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
			var row = new VisualElement();
			row.AddToClassList("goals-row");
			row.EnableInClassList("goals-row--selected", entry.EntityId == _selectedOrgId);
			string orgId = entry.EntityId;
			row.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && row.ContainsPoint(e.localPosition)) {
					SelectOrg(orgId);
				}
			});

			var place = new Label(entry.Place.ToString(CultureInfo.InvariantCulture));
			place.AddToClassList("goals-row-place");
			row.Add(place);

			var flag = new VisualElement();
			flag.AddToClassList("goals-row-flag");
			Sprite sprite = _orgVisualConfig?.Find(entry.EntityId)?.flag;
			if (sprite != null) {
				flag.style.backgroundImage = new StyleBackground(sprite);
				flag.style.display = DisplayStyle.Flex;
			} else {
				flag.style.display = DisplayStyle.None;
			}
			row.Add(flag);

			var name = new Label(entry.DisplayName);
			name.AddToClassList("goals-row-name");
			row.Add(name);

			var score = new Label(ScoreFormat.Format(entry.Score));
			score.AddToClassList("goals-row-score");
			row.Add(score);

			return row;
		}

		VisualElement CreateProgressRow(GoalProgressEntryState goal) {
			var row = new VisualElement();
			row.AddToClassList("goals-progress-row");

			var description = new Label(FormatDescription(goal));
			description.AddToClassList("goals-progress-description");
			row.Add(description);

			var meter = new VisualElement();
			meter.AddToClassList("goals-progress-meter");

			var track = new VisualElement();
			track.AddToClassList("goals-progress-track");
			var fill = new VisualElement();
			fill.AddToClassList("goals-progress-fill");
			float percent = 0f;
			if (goal.Target > 0) {
				percent = (float)Math.Min(1.0, goal.Current / goal.Target) * 100f;
			}
			fill.style.width = new Length(percent, LengthUnit.Percent);
			track.Add(fill);
			meter.Add(track);

			var nm = new Label($"{FormatValue(goal, goal.Current)}/{FormatValue(goal, goal.Target)}");
			nm.AddToClassList("goals-progress-nm");
			meter.Add(nm);

			row.Add(meter);
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
