using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class SelectOrgDocument : MonoBehaviour {
		SelectOrgLogic _logic;
		SceneLoader _sceneLoader;
		ILocalization _localization;
		UIDocument _doc;
		Label _orgNameLabel;
		Label _goldLabel;
		Label _hintLabel;
		Button _btnStart;

		[Inject]
		void Construct(SelectOrgLogic logic, SceneLoader sceneLoader, ILocalization localization) {
			_logic = logic;
			_sceneLoader = sceneLoader;
			_localization = localization;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void Start() {
			var root = _doc.rootVisualElement;
			_orgNameLabel = root.Q<Label>("country-name-label");
			_goldLabel = root.Q<Label>("gold-label");
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
				if (_goldLabel != null) {
					_goldLabel.text = $"{_localization.Get("select_org.gold")}: {state.InitialGold:F0}";
				}
				_hintLabel.style.opacity = 0;
				_btnStart.SetEnabled(true);
			} else {
				_orgNameLabel.text = "";
				if (_goldLabel != null) {
					_goldLabel.text = "";
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
