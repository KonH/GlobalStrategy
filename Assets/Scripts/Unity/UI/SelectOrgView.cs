using System.Globalization;
using GS.Main;
using GS.Unity.Map;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Plain view for SelectOrgDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). Owns the info panel and win-condition hint display; the document
	/// keeps SelectOrgLogic polling, DI, and OnClick wiring via the exposed buttons.
	/// </summary>
	public class SelectOrgView {
		readonly ILocalization _loc;
		readonly Label _orgNameLabel;
		readonly VisualElement _orgFlagElement;
		readonly Label _goldLabel;
		readonly Label _controlLabel;
		readonly Label _estimatedIncomeLabel;
		readonly Label _hintLabel;
		readonly Button _btnStart;
		readonly Button _btnBack;
		readonly Label _winConditionsHeader;
		readonly VisualElement _goalHintRows;
		readonly Label _goalHintAlternativeCue;
		readonly Label _goalHintEmpty;

		public SelectOrgView(VisualElement root, ILocalization loc) {
			_loc = loc;
			_orgNameLabel = root.Q<Label>("country-name-label");
			_orgFlagElement = root.Q("org-flag");
			_goldLabel = root.Q<Label>("gold-label");
			_controlLabel = root.Q<Label>("control-label");
			_estimatedIncomeLabel = root.Q<Label>("estimated-income-label");
			_hintLabel = root.Q<Label>("hint-label");
			_btnStart = root.Q<Button>("btn-start");
			_btnBack = root.Q<Button>("btn-back");
			_winConditionsHeader = root.Q<Label>("win_conditions-header");
			_goalHintRows = root.Q<VisualElement>("goal-hint-rows");
			_goalHintAlternativeCue = root.Q<Label>("goal-hint-alternative-cue");
			_goalHintEmpty = root.Q<Label>("goal-hint-empty");
		}

		public Button BtnStart => _btnStart;
		public Button BtnBack => _btnBack;

		public void RefreshTexts() {
			_hintLabel.text = _loc.Get("select_org.hint");
			_btnStart.text = _loc.Get("select_org.start");
			if (_btnBack != null) {
				_btnBack.text = _loc.Get("select_org.back");
			}
			if (_winConditionsHeader != null) {
				_winConditionsHeader.text = _loc.Get("select_org.win_conditions.header");
			}
		}

		public void RefreshGoalHint(WinConditionHintState hint) {
			if (_goalHintRows == null) {
				return;
			}
			_goalHintRows.Clear();
			bool showRows = hint != null && hint.IsAvailable && hint.Rows.Count > 0;
			if (hint != null) {
				foreach (var row in hint.Rows) {
					var label = new Label(FormatGoalHintRow(row));
					label.AddToClassList("goal-hint-row");
					_goalHintRows.Add(label);
				}
			}
			if (_goalHintAlternativeCue != null) {
				_goalHintAlternativeCue.style.display = hint != null && hint.IsAlternativeGroup ? DisplayStyle.Flex : DisplayStyle.None;
				_goalHintAlternativeCue.text = _loc.Get("select_org.win_conditions.alternative_cue");
			}
			if (_goalHintEmpty != null) {
				_goalHintEmpty.style.display = showRows ? DisplayStyle.None : DisplayStyle.Flex;
				_goalHintEmpty.text = _loc.Get("select_org.win_conditions.empty");
			}
		}

		string FormatGoalHintRow(WinConditionHintRowState row) {
			switch (row.Kind) {
				case WinConditionHintKind.TotalControl:
					return string.Format(
						_loc.Get("select_org.win_conditions.total_control"),
						(row.Value * 100).ToString("0", CultureInfo.InvariantCulture));
				case WinConditionHintKind.FullControlCountries:
					return string.Format(
						_loc.Get("select_org.win_conditions.full_control_countries"),
						((int)row.Value).ToString(CultureInfo.InvariantCulture),
						row.AvailableCountryCount.ToString(CultureInfo.InvariantCulture));
				case WinConditionHintKind.ScoreGoal:
					return string.Format(
						_loc.Get("select_org.win_conditions.score_goal"),
						ScoreFormat.Format(row.Value));
				case WinConditionHintKind.LastOrgStanding:
					return _loc.Get("select_org.win_conditions.last_org_standing");
				default:
					return "";
			}
		}

		public void Refresh(SelectedOrganizationState state, OrgVisualConfig orgVisualConfig, int baseControl, double estimatedIncome) {
			if (state.IsValid) {
				_orgNameLabel.text = state.DisplayName;
				if (_orgFlagElement != null) {
					var sprite = orgVisualConfig?.Find(state.OrgId)?.flag;
					if (sprite != null) {
						_orgFlagElement.style.backgroundImage = new StyleBackground(sprite);
						_orgFlagElement.style.display = DisplayStyle.Flex;
					} else {
						_orgFlagElement.style.display = DisplayStyle.None;
					}
				}
				if (_goldLabel != null) {
					_goldLabel.text = $"{_loc.Get("select_org.gold")}: {state.InitialGold:F0}";
				}
				if (_controlLabel != null) {
					_controlLabel.text = $"{_loc.Get("select_org.base_control")} {baseControl}/100";
				}
				if (_estimatedIncomeLabel != null) {
					_estimatedIncomeLabel.text = $"{_loc.Get("select_org.estimated_income")} +{estimatedIncome:F1}/month";
				}
				_hintLabel.style.opacity = 0;
				_btnStart.SetEnabled(true);
			} else {
				_orgNameLabel.text = "";
				if (_orgFlagElement != null) { _orgFlagElement.style.display = DisplayStyle.None; }
				if (_goldLabel != null) {
					_goldLabel.text = "";
				}
				if (_controlLabel != null) {
					_controlLabel.text = "";
				}
				if (_estimatedIncomeLabel != null) {
					_estimatedIncomeLabel.text = "";
				}
				_hintLabel.style.opacity = 1;
				_btnStart.SetEnabled(false);
			}
		}
	}
}
