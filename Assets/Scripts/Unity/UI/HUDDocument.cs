using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;

namespace GS.Unity.UI {
	public class HUDDocument : MonoBehaviour {
		UIDocument _document;
		CountryInfoView _countryInfo;
		VisualState _state;

		[Inject]
		void Construct(VisualState state) {
			_state = state;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			_countryInfo = new CountryInfoView(root.Q("country-info"));
		}

		void OnEnable() {
			if (_state == null) return;
			_state.SelectedCountry.PropertyChanged += HandleCountryChanged;
			_countryInfo.Refresh(_state.SelectedCountry);
		}

		void OnDisable() {
			if (_state == null) return;
			_state.SelectedCountry.PropertyChanged -= HandleCountryChanged;
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo.Refresh(_state.SelectedCountry);
		}
	}
}
