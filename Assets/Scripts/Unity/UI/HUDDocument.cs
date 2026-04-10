using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;

namespace GS.Unity.UI {
	public class HUDDocument : MonoBehaviour {
		UIDocument _document;
		CountryInfoView _countryInfo;
		PlayerCountryView _playerCountryView;
		TimeView _timeView;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;
		ResourceConfig _resourceConfig;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_resourceConfig = resourceConfig;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			if (_loc == null) {
				Debug.LogWarning("[HUDDocument] _loc is null in Awake — injection has not happened yet");
			}

			var tooltip = new TooltipController(root.Q("tooltip-overlay"));

			_countryInfo = new CountryInfoView(root.Q("country-info"), _loc, _resourceConfig, tooltip);
			_countryInfo.OnSelectClicked = OnSelectPlayerCountry;
			_playerCountryView = new PlayerCountryView(root.Q("player-country"), _loc, _resourceConfig, tooltip);
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
			_state.PlayerResources.PropertyChanged += HandlePlayerResourcesChanged;
			_state.SelectedResources.PropertyChanged += HandleSelectedResourcesChanged;
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
			_state.PlayerResources.PropertyChanged -= HandlePlayerResourcesChanged;
			_state.SelectedResources.PropertyChanged -= HandleSelectedResourcesChanged;
		}

		void RefreshCountryViews() {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry, _state.SelectedResources);
			_playerCountryView.Refresh(_state.PlayerCountry, _state.PlayerResources);
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

		void HandlePlayerResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_playerCountryView.Refresh(_state.PlayerCountry, _state.PlayerResources);
		}

		void HandleSelectedResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry, _state.SelectedResources);
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
