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
		PlayerCountryView _playerCountryView;
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
			if (_loc == null) {
				Debug.LogWarning("[HUDDocument] _loc is null in Awake — injection has not happened yet");
			}
			_countryInfo = new CountryInfoView(root.Q("country-info"), _loc);
			_countryInfo.OnSelectClicked = OnSelectPlayerCountry;
			_playerCountryView = new PlayerCountryView(root.Q("player-country"), _loc);
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
			_state.PlayerCountry.PropertyChanged += HandlePlayerCountryChanged;
			_state.Time.PropertyChanged += HandleTimeChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			RefreshCountryViews();
			_timeView.Refresh(_state.Time);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged -= HandleCountryChanged;
			_state.PlayerCountry.PropertyChanged -= HandlePlayerCountryChanged;
			_state.Time.PropertyChanged -= HandleTimeChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
		}

		void RefreshCountryViews() {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry);
			_playerCountryView.Refresh(_state.PlayerCountry);
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandlePlayerCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandleTimeChanged(object sender, PropertyChangedEventArgs e) {
			_timeView.Refresh(_state.Time);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			_loc.SetLocale(_state.Locale.Locale);
			RefreshCountryViews();
			_timeView.Refresh(_state.Time);
		}

		void OnSelectPlayerCountry() {
			if (_state.SelectedCountry.IsValid) {
				_commands.Push(new SelectPlayerCountryCommand(_state.SelectedCountry.CountryId));
			}
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
