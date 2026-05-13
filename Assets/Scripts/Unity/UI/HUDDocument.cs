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
		PlayerOrgView _playerOrgView;
		TimeView _timeView;
		TooltipSystem _tooltip;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;
		ResourceConfig _resourceConfig;
		CharacterConfig _characterConfig;
		GameMenuDocument _gameMenu;
		Button _btnMenu;
		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnEcsViewer;
		VisualElement _influenceDebugRow;
		bool _debugPanelOpen;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, GameMenuDocument gameMenu) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_gameMenu = gameMenu;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var root = _document.rootVisualElement;
			if (_loc == null) {
				Debug.LogWarning("[HUDDocument] _loc is null in Awake — injection has not happened yet");
			}

			_tooltip = new TooltipSystem(root.Q("hud-root"));

			_countryInfo = new CountryInfoView(root.Q("country-info"), _loc, _resourceConfig, _characterConfig, _tooltip);
			_playerOrgView = new PlayerOrgView(root.Q("player-country"), _loc, _resourceConfig, _tooltip);
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

			_influenceDebugRow = root.Q("influence-debug-row");
			var btnInfluencePlus  = root.Q<Button>("btn-influence-plus");
			var btnInfluenceMinus = root.Q<Button>("btn-influence-minus");
			if (btnInfluencePlus != null) {
				btnInfluencePlus.clicked += () => PushInfluenceCommand(+5);
			}
			if (btnInfluenceMinus != null) {
				btnInfluenceMinus.clicked += () => PushInfluenceCommand(-5);
			}
			RefreshInfluenceDebugRow();
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
			_state.SelectedCountry.PropertyChanged    += HandleCountryChanged;
			_state.PlayerOrganization.PropertyChanged += HandlePlayerOrgChanged;
			_state.Time.PropertyChanged               += HandleTimeChanged;
			_state.Locale.PropertyChanged             += HandleLocaleChanged;
			_state.PlayerResources.PropertyChanged    += HandlePlayerResourcesChanged;
			_state.SelectedResources.PropertyChanged  += HandleSelectedResourcesChanged;
			_state.SelectedInfluence.PropertyChanged  += HandleInfluenceChanged;
			_state.SelectedCharacters.PropertyChanged += HandleCharactersChanged;
			RefreshCountryViews();
			RefreshInfluenceDebugRow();
			_timeView.Refresh(_state.Time);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged    -= HandleCountryChanged;
			_state.PlayerOrganization.PropertyChanged -= HandlePlayerOrgChanged;
			_state.Time.PropertyChanged               -= HandleTimeChanged;
			_state.Locale.PropertyChanged             -= HandleLocaleChanged;
			_state.PlayerResources.PropertyChanged    -= HandlePlayerResourcesChanged;
			_state.SelectedResources.PropertyChanged  -= HandleSelectedResourcesChanged;
			_state.SelectedInfluence.PropertyChanged  -= HandleInfluenceChanged;
			_state.SelectedCharacters.PropertyChanged -= HandleCharactersChanged;
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
		}

		void RefreshCountryViews() {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry, _state.SelectedResources, _state.SelectedInfluence, _state.SelectedCharacters);
			_playerOrgView.Refresh(_state.PlayerOrganization, _state.PlayerResources);
		}

		void RefreshInfluenceDebugRow() {
			if (_influenceDebugRow == null) {
				return;
			}
			_influenceDebugRow.style.display =
				_state != null && _state.SelectedCountry.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void PushInfluenceCommand(int delta) {
			if (_state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) {
				return;
			}
			_commands.Push(new ChangeInfluenceCommand {
				OrgId     = _state.PlayerOrganization.OrgId,
				CountryId = _state.SelectedCountry.CountryId,
				Delta     = delta
			});
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			RefreshInfluenceDebugRow();
		}

		void HandlePlayerOrgChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandleInfluenceChanged(object sender, PropertyChangedEventArgs e) {
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
			_playerOrgView.Refresh(_state.PlayerOrganization, _state.PlayerResources);
		}

		void HandleSelectedResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry, _state.SelectedResources, _state.SelectedInfluence, _state.SelectedCharacters);
		}

		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
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
