using UnityEngine;
using UnityEngine.UIElements;
using GS.Unity.VisualState;

namespace GS.Prototype.UIToolkit {
	public class CountryInfoUIDocument : MonoBehaviour {
		[SerializeField] VisualStateHolder _stateHolder;

		UIDocument _document;
		VisualElement _panel;
		Label _countryNameLabel;

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			_panel = root.Q<VisualElement>("panel");
			_countryNameLabel = root.Q<Label>("country-name");
		}

		void OnEnable() {
			_stateHolder.State.SelectedCountry.OnChanged += HandleCountryStateChanged;
			Refresh(_stateHolder.State.SelectedCountry);
		}

		void OnDisable() {
			_stateHolder.State.SelectedCountry.OnChanged -= HandleCountryStateChanged;
		}

		void HandleCountryStateChanged(CountryState state) => Refresh(state);

		void Refresh(CountryState state) {
			_panel.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			_countryNameLabel.text = state.CountryName ?? string.Empty;
		}
	}
}
