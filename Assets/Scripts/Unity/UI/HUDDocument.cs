using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.EcsViewer;

namespace GS.Unity.UI {
	public class HUDDocument : MonoBehaviour {
		UIDocument _document;
		CountryInfoView _countryInfo;
		PlayerCountryView _playerCountryView;
		TimeView _timeView;
		TooltipSystem _tooltip;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;
		ResourceConfig _resourceConfig;
		GameMenuDocument _gameMenu;
		Button _btnMenu;
		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnEcsViewer;
		bool _debugPanelOpen;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, GameMenuDocument gameMenu) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_gameMenu = gameMenu;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			if (_loc == null) {
				Debug.LogWarning("[HUDDocument] _loc is null in Awake — injection has not happened yet");
			}

			_tooltip = new TooltipSystem(root.Q("hud-root"));

			_countryInfo = new CountryInfoView(root.Q("country-info"), _loc, _resourceConfig, _tooltip);
			_playerCountryView = new PlayerCountryView(root.Q("player-country"), _loc, _resourceConfig, _tooltip);
			_timeView = new TimeView(
				root.Q("time-panel"),
				OnPauseToggle,
				OnSpeedChange);
		}

		void Start() {
			var root = _document.rootVisualElement;
			_btnMenu = root.Q<Button>("btn-menu");
			_btnMenu.clicked += () => _gameMenu?.Show();

			_btnDebugToggle = root.Q<Button>("btn-debug-toggle");
			_debugPanel = root.Q("debug-panel");
			_btnEcsViewer = root.Q<Button>("btn-ecs-viewer");

			_btnDebugToggle.clicked += ToggleDebugPanel;
			_btnEcsViewer.clicked += OpenEcsViewer;
#if UNITY_WEBGL
			_btnEcsViewer.style.display = DisplayStyle.None;
#endif
		}

		void ToggleDebugPanel() {
			_debugPanelOpen = !_debugPanelOpen;
			_debugPanel.style.display = _debugPanelOpen ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void OpenEcsViewer() {
			var url = EcsViewerBridge.CurrentUrl;
			if (url == null) {
				Debug.LogWarning("[HUDDocument] ECS Viewer URL is not available — is the bridge running?");
				return;
			}
			Application.OpenURL(url);
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

		void Update() {
			_tooltip?.Update(Time.deltaTime);
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
