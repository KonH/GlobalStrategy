using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class SelectCountryDocument : MonoBehaviour {
		SelectCountryLogic _logic;
		SceneLoader _sceneLoader;
		ILocalization _localization;
		UIDocument _doc;
		Label _countryNameLabel;
		Label _hintLabel;
		Button _btnStart;

		[Inject]
		void Construct(SelectCountryLogic logic, SceneLoader sceneLoader, ILocalization localization) {
			_logic = logic;
			_sceneLoader = sceneLoader;
			_localization = localization;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void Start() {
			var root = _doc.rootVisualElement;
			_countryNameLabel = root.Q<Label>("country-name-label");
			_hintLabel = root.Q<Label>("hint-label");
			_btnStart = root.Q<Button>("btn-start");
			var btnBack = root.Q<Button>("btn-back");
			btnBack.clicked += () => _sceneLoader.LoadMainMenu();
			_btnStart.clicked += OnStartGame;
			_btnStart.SetEnabled(false);

			_hintLabel.text = _localization.Get("select_country.hint");
			_btnStart.text = _localization.Get("select_country.start");
			btnBack.text = _localization.Get("select_country.back");

			_logic.VisualState.SelectedCountry.PropertyChanged += (_, _) => RefreshUI();
			RefreshUI();
		}

		void Update() {
			_logic.Update();
		}

		void RefreshUI() {
			var state = _logic.VisualState.SelectedCountry;
			if (state.IsValid) {
				_countryNameLabel.text = _localization.Get($"country_name.{state.CountryId}");
				_hintLabel.style.opacity = 0;
				_btnStart.SetEnabled(true);
			} else {
				_countryNameLabel.text = "";
				_hintLabel.style.opacity = 1;
				_btnStart.SetEnabled(false);
			}
		}

		void OnStartGame() {
			var countryId = _logic.VisualState.SelectedCountry.CountryId;
			if (!string.IsNullOrEmpty(countryId)) {
				_sceneLoader.LoadGame(playerCountryId: countryId);
			}
		}
	}
}
