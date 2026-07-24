using System.Globalization;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class SelectOrgDocument : MonoBehaviour {
		SelectOrgLogic _logic;
		SceneLoader _sceneLoader;
		ILocalization _localization;
		OrgVisualConfig _orgVisualConfig;
		UIDocument _doc;
		Label _orgNameLabel;
		VisualElement _orgFlagElement;
		Label _goldLabel;
		Label _controlLabel;
		Label _estimatedIncomeLabel;
		Label _hintLabel;
		Button _btnStart;
		VisualElement _goalHintRows;
		Label _goalHintAlternativeCue;
		Label _goalHintEmpty;

		[Inject]
		void Construct(SelectOrgLogic logic, SceneLoader sceneLoader, ILocalization localization, OrgVisualConfig orgVisualConfig) {
			_logic = logic;
			_sceneLoader = sceneLoader;
			_localization = localization;
			_orgVisualConfig = orgVisualConfig;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void Start() {
			var root = _doc.rootVisualElement;
			_orgNameLabel = root.Q<Label>("country-name-label");
			_orgFlagElement = root.Q("org-flag");
			_goldLabel = root.Q<Label>("gold-label");
			_controlLabel = root.Q<Label>("control-label");
			_estimatedIncomeLabel = root.Q<Label>("estimated-income-label");
			_hintLabel = root.Q<Label>("hint-label");
			_btnStart = root.Q<Button>("btn-start");
			var btnBack = root.Q<Button>("btn-back");
			_goalHintRows = root.Q<VisualElement>("goal-hint-rows");
			_goalHintAlternativeCue = root.Q<Label>("goal-hint-alternative-cue");
			_goalHintEmpty = root.Q<Label>("goal-hint-empty");

			btnBack.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && btnBack.ContainsPoint(e.localPosition)) {
					_sceneLoader.LoadMainMenu();
				}
			});
			_btnStart.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnStart.ContainsPoint(e.localPosition)) {
					OnStartGame();
				}
			});
			_btnStart.SetEnabled(false);

			RefreshTexts();

			_logic.VisualState.SelectedOrganization.PropertyChanged += (_, _) => RefreshUI();
			RefreshUI();
		}

		void RefreshTexts() {
			var root = _doc.rootVisualElement;
			_hintLabel.text = _localization.Get("select_org.hint");
			_btnStart.text = _localization.Get("select_org.start");
			root.Q<Button>("btn-back").text = _localization.Get("select_org.back");
			root.Q<Label>("win_conditions-header").text = _localization.Get("select_org.win_conditions.header");
			RefreshGoalHint();
		}

		void RefreshGoalHint() {
			if (_goalHintRows == null) {
				return;
			}
			var hint = _logic.VisualState.WinConditionHint;
			_goalHintRows.Clear();
			bool showRows = hint.IsAvailable && hint.Rows.Count > 0;
			foreach (var row in hint.Rows) {
				var label = new Label(FormatGoalHintRow(row));
				label.AddToClassList("goal-hint-row");
				_goalHintRows.Add(label);
			}
			if (_goalHintAlternativeCue != null) {
				_goalHintAlternativeCue.style.display = hint.IsAlternativeGroup ? DisplayStyle.Flex : DisplayStyle.None;
				_goalHintAlternativeCue.text = _localization.Get("select_org.win_conditions.alternative_cue");
			}
			if (_goalHintEmpty != null) {
				_goalHintEmpty.style.display = showRows ? DisplayStyle.None : DisplayStyle.Flex;
				_goalHintEmpty.text = _localization.Get("select_org.win_conditions.empty");
			}
		}

		string FormatGoalHintRow(WinConditionHintRowState row) {
			switch (row.Kind) {
				case WinConditionHintKind.TotalControl:
					return string.Format(
						_localization.Get("select_org.win_conditions.total_control"),
						(row.Value * 100).ToString("0", CultureInfo.InvariantCulture));
				case WinConditionHintKind.FullControlCountries:
					return string.Format(
						_localization.Get("select_org.win_conditions.full_control_countries"),
						((int)row.Value).ToString(CultureInfo.InvariantCulture),
						row.AvailableCountryCount.ToString(CultureInfo.InvariantCulture));
				default:
					return "";
			}
		}

		void Update() {
			_logic.Update();
		}

		void RefreshUI() {
			var state = _logic.VisualState.SelectedOrganization;
			if (state.IsValid) {
				_orgNameLabel.text = state.DisplayName;
				if (_orgFlagElement != null) {
					var sprite = _orgVisualConfig?.Find(state.OrgId)?.flag;
					if (sprite != null) {
						_orgFlagElement.style.backgroundImage = new StyleBackground(sprite);
						_orgFlagElement.style.display = DisplayStyle.Flex;
					} else {
						_orgFlagElement.style.display = DisplayStyle.None;
					}
				}
				if (_goldLabel != null) {
					_goldLabel.text = $"{_localization.Get("select_org.gold")}: {state.InitialGold:F0}";
				}
				if (_controlLabel != null) {
					int baseControl = _logic.GetBaseControl(state.OrgId);
					_controlLabel.text = $"{_localization.Get("select_org.base_control")} {baseControl}/100";
				}
				if (_estimatedIncomeLabel != null) {
					double income = _logic.ComputeBaseControlIncome(state.OrgId);
					_estimatedIncomeLabel.text = $"{_localization.Get("select_org.estimated_income")} +{income:F1}/month";
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

		void OnStartGame() {
			var orgState = _logic.VisualState.SelectedOrganization;
			if (orgState.IsValid) {
				_sceneLoader.LoadGame(organizationId: orgState.OrgId);
			}
		}
	}
}
