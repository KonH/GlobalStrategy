using System.ComponentModel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.EcsViewer;
using GS.Unity.Common;

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
		CharacterVisualConfig _characterVisualConfig;
		GameMenuDocument _gameMenu;
		Button _btnMenu;
		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnEcsViewer;
		VisualElement _influenceDebugRow;
		bool _debugPanelOpen;
		OrgInfoDocument _orgInfoDocument;
		VisualElement _root;
		VisualElement _countryInfoRoot;
		int _lastOrgAgentSlotCount = -1;
		bool _orgPanelOpen;
		LensSwitcherView _lensSwitcher;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig, GameMenuDocument gameMenu, OrgInfoDocument orgInfoDocument) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
			_gameMenu = gameMenu;
			_orgInfoDocument = orgInfoDocument;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			_root = _document.rootVisualElement;
			var root = _root;
			if (_loc == null) {
				Debug.LogWarning("[HUDDocument] _loc is null in Awake — injection has not happened yet");
			}

			_tooltip = new TooltipSystem(root.Q("hud-root"));

			_countryInfoRoot = root.Q("country-info");
			_countryInfo = new CountryInfoView(_countryInfoRoot, _loc, _resourceConfig, _characterConfig, _tooltip, _characterVisualConfig);
			_playerOrgView = new PlayerOrgView(root.Q("player-country"), _loc, _resourceConfig, _tooltip);
			_timeView = new TimeView(
				root.Q("time-panel"),
				OnPauseToggle,
				OnSpeedChange);
			_lensSwitcher = new LensSwitcherView(root.Q("lens-switcher"), _tooltip, _loc);
			_lensSwitcher.OnLensSelected = OnLensSelected;

			var playerOrgRoot = root.Q("player-country");
			if (playerOrgRoot != null) {
				playerOrgRoot.RegisterCallback<ClickEvent>(_ => ToggleOrgInfo());
			}
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

			// Country character debug buttons
			var characterDebugContainer = root.Q("character-debug-container");
			if (characterDebugContainer != null && _characterConfig != null) {
				foreach (var role in _characterConfig.Roles) {
					bool usedInCountryPool = false;
					foreach (var cp in _characterConfig.CountryPools) {
						if (cp.Slots.ContainsKey(role.RoleId)) { usedInCountryPool = true; break; }
					}
					if (!usedInCountryPool) { continue; }
					string capturedRoleId = role.RoleId;
					var nextBtn = new Button(() => PushCycleCharacter(_state?.PlayerCountry?.CountryId ?? "", capturedRoleId, 0));
					nextBtn.text = $"Next: {role.RoleId}";
					nextBtn.AddToClassList("gs-btn");
					nextBtn.AddToClassList("gs-btn--small");
					nextBtn.AddToClassList("debug-panel-button");
					characterDebugContainer.Add(nextBtn);

					var dropBtn = new Button(() => PushDropCharacter(_state?.PlayerCountry?.CountryId ?? "", capturedRoleId, 0));
					dropBtn.text = $"Drop: {role.RoleId}";
					dropBtn.AddToClassList("gs-btn");
					dropBtn.AddToClassList("gs-btn--small");
					dropBtn.AddToClassList("debug-panel-button");
					characterDebugContainer.Add(dropBtn);
				}
			}

			RebuildOrgCharDebugButtons();
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
			_state.MapLens.PropertyChanged            += HandleLensChanged;
			_state.PlayerOrgCharacters.PropertyChanged += HandleOrgCharactersChanged;
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
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
			_state.MapLens.PropertyChanged            -= HandleLensChanged;
			_state.PlayerOrgCharacters.PropertyChanged -= HandleOrgCharactersChanged;
			_lastOrgAgentSlotCount = -1;
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
			if (_orgPanelOpen) {
				var mouse = UnityEngine.InputSystem.Mouse.current;
				if (mouse != null && mouse.leftButton.wasPressedThisFrame) {
					if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) {
						_orgPanelOpen = false;
						_orgInfoDocument?.Hide();
						RefreshCountryViews();
					}
				}
			}
		}

		void RefreshCountryViews() {
			_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry, _state.SelectedResources, _state.SelectedInfluence, _state.SelectedCharacters);
			_playerOrgView.Refresh(_state.PlayerOrganization, _state.PlayerResources);
			if (_orgPanelOpen && _countryInfoRoot != null) {
				_countryInfoRoot.style.display = DisplayStyle.None;
			}
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

		void HandleLensChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
		}

		void OnLensSelected(MapLens lens) {
			_commands.Push(new ChangeLensCommand { Lens = lens });
		}

		void ToggleOrgInfo() {
			if (_orgInfoDocument == null) { return; }
			_orgPanelOpen = !_orgPanelOpen;
			if (_orgPanelOpen) {
				_orgInfoDocument.Show();
				if (_countryInfoRoot != null) { _countryInfoRoot.style.display = DisplayStyle.None; }
			} else {
				_orgInfoDocument.Hide();
				RefreshCountryViews();
			}
		}

		void RebuildOrgCharDebugButtons() {
			var orgCharDebugContainer = _root?.Q("org-char-debug-container");
			if (orgCharDebugContainer == null) { return; }
			orgCharDebugContainer.Clear();

			var masterNextBtn = new Button(() => PushCycleCharacter(GetPlayerOrgId(), "master", 0));
			masterNextBtn.text = "Next: master";
			masterNextBtn.AddToClassList("gs-btn");
			masterNextBtn.AddToClassList("gs-btn--small");
			masterNextBtn.AddToClassList("debug-panel-button");
			orgCharDebugContainer.Add(masterNextBtn);

			var masterDropBtn = new Button(() => PushDropCharacter(GetPlayerOrgId(), "master", 0));
			masterDropBtn.text = "Drop: master";
			masterDropBtn.AddToClassList("gs-btn");
			masterDropBtn.AddToClassList("gs-btn--small");
			masterDropBtn.AddToClassList("debug-panel-button");
			orgCharDebugContainer.Add(masterDropBtn);

			int agentCount = 0;
			if (_state?.PlayerOrgCharacters?.Slots != null) {
				foreach (var slot in _state.PlayerOrgCharacters.Slots) {
					if (slot.RoleId == "agent") { agentCount++; }
				}
			}
			for (int si = 0; si < agentCount; si++) {
				int capturedSlot = si;
				var agentNextBtn = new Button(() => PushCycleCharacter(GetPlayerOrgId(), "agent", capturedSlot));
				agentNextBtn.text = $"Next: agent [{capturedSlot + 1}]";
				agentNextBtn.AddToClassList("gs-btn");
				agentNextBtn.AddToClassList("gs-btn--small");
				agentNextBtn.AddToClassList("debug-panel-button");
				orgCharDebugContainer.Add(agentNextBtn);

				var agentDropBtn = new Button(() => PushDropCharacter(GetPlayerOrgId(), "agent", capturedSlot));
				agentDropBtn.text = $"Drop: agent [{capturedSlot + 1}]";
				agentDropBtn.AddToClassList("gs-btn");
				agentDropBtn.AddToClassList("gs-btn--small");
				agentDropBtn.AddToClassList("debug-panel-button");
				orgCharDebugContainer.Add(agentDropBtn);
			}
		}

		string GetPlayerOrgId() => _state?.PlayerOrganization?.OrgId ?? "";

		void PushCycleCharacter(string ownerId, string roleId, int slotIndex) {
			if (string.IsNullOrEmpty(ownerId) || _commands == null) { return; }
			_commands.Push(new DebugCycleCharacterCommand { OwnerId = ownerId, RoleId = roleId, SlotIndex = slotIndex });
		}

		void PushDropCharacter(string ownerId, string roleId, int slotIndex) {
			if (string.IsNullOrEmpty(ownerId) || _commands == null) { return; }
			_commands.Push(new DebugDropCharacterCommand { OwnerId = ownerId, RoleId = roleId, SlotIndex = slotIndex });
		}

		void HandleOrgCharactersChanged(object sender, PropertyChangedEventArgs e) {
			int agentCount = 0;
			if (_state?.PlayerOrgCharacters?.Slots != null) {
				foreach (var slot in _state.PlayerOrgCharacters.Slots) {
					if (slot.RoleId == "agent") { agentCount++; }
				}
			}
			if (agentCount == _lastOrgAgentSlotCount) { return; }
			_lastOrgAgentSlotCount = agentCount;
			RebuildOrgCharDebugButtons();
		}
	}
}
