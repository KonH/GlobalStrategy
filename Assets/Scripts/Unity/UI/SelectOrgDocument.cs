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
		Label _influenceLabel;
		Label _estimatedIncomeLabel;
		Label _hintLabel;
		Button _btnStart;

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
			_influenceLabel = root.Q<Label>("influence-label");
			_estimatedIncomeLabel = root.Q<Label>("estimated-income-label");
			_hintLabel = root.Q<Label>("hint-label");
			_btnStart = root.Q<Button>("btn-start");
			var btnBack = root.Q<Button>("btn-back");
			btnBack.clicked += () => _sceneLoader.LoadMainMenu();
			_btnStart.clicked += OnStartGame;
			_btnStart.SetEnabled(false);

			_hintLabel.text = _localization.Get("select_org.hint");
			_btnStart.text = _localization.Get("select_org.start");
			btnBack.text = _localization.Get("select_org.back");

			_logic.VisualState.SelectedOrganization.PropertyChanged += (_, _) => RefreshUI();
			RefreshUI();
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
				if (_influenceLabel != null) {
					int baseInfluence = _logic.GetBaseInfluence(state.OrgId);
					_influenceLabel.text = $"{_localization.Get("select_org.base_influence")} {baseInfluence}/100";
				}
				if (_estimatedIncomeLabel != null) {
					double income = _logic.ComputeBaseInfluenceIncome(state.OrgId);
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
				if (_influenceLabel != null) {
					_influenceLabel.text = "";
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
			var countryState = _logic.VisualState.SelectedCountry;
			if (orgState.IsValid) {
				_sceneLoader.LoadGame(playerCountryId: countryState.CountryId, organizationId: orgState.OrgId);
			}
		}
	}
}
