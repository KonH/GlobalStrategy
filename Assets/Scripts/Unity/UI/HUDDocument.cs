using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;

namespace GS.Unity.UI {
	public class HUDDocument : MonoBehaviour {
		UIDocument _document;
		CountryInfoView _countryInfo;
		TimeView _timeView;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc) {
			_state = state;
			_commands = commands;
			_loc = loc;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			_countryInfo = new CountryInfoView(root.Q("country-info"), _loc);
			_timeView = new TimeView(
				root.Q("time-panel"),
				OnPauseToggle,
				OnSpeedChange);
		}

		void OnEnable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged += HandleCountryChanged;
			_state.Time.PropertyChanged += HandleTimeChanged;
			_countryInfo.Refresh(_state.SelectedCountry);
			_timeView.Refresh(_state.Time);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged -= HandleCountryChanged;
			_state.Time.PropertyChanged -= HandleTimeChanged;
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo.Refresh(_state.SelectedCountry);
		}

		void HandleTimeChanged(object sender, PropertyChangedEventArgs e) {
			_timeView.Refresh(_state.Time);
		}

		void OnPauseToggle() {
			if (_state.Time.IsPaused) {
				_commands.Push(new UnpauseCommand());
			} else {
				_commands.Push(new PauseCommand());
			}
		}

		void OnSpeedChange(int index) {
			_commands.Push(new ChangeTimeMultiplierCommand(index));
		}
	}
}
