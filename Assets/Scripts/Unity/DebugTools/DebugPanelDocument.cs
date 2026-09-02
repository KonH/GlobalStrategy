using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Unity.EcsViewer;
using GS.Unity.UI;

namespace GS.Unity.DebugTools {
	// Debug UI extraction (Docs/Specs/26_08_28_16_ui-refactoring phase 5): this document owns the
	// in-HUD debug tooling (province/relation/control-org/character cheats, gold buttons, FPS
	// counter, ECS viewer link) that used to live inside HUDDocument. It is its own UIDocument,
	// sharing HUDPanelSettings with a sortingOrder placing it alongside the HUD, and subscribes to
	// VisualState directly instead of going through HUDDocument - the two documents don't know
	// about each other.
	public class DebugPanelDocument : MonoBehaviour {
		UIDocument _document;
		VisualElement _root;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;
		CharacterConfig _characterConfig;
		ActionConfig _actionConfig;
		CountryConfig _countryConfig;
		GameSettings _gameSettings;
		GameLogic _gameLogic;

		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnSelectedCountryDebugMenu;
		VisualElement _selectedCountryDebugMenu;
		Button _btnMyOrgDebugMenu;
		VisualElement _myOrgDebugMenu;
		Button _btnSelectedOrgDebugMenu;
		VisualElement _selectedOrgDebugMenu;
		Button _btnEcsViewer;
		bool _debugPanelOpen;
		Button _btnFpsToggle;
		Label _fpsLabel;
		bool _fpsEnabled;
		readonly Queue<float> _fpsFrameTimestamps = new();
		Button _btnSelectedProvinceDebugMenu;
		VisualElement _selectedProvinceDebugMenu;
		VisualElement _provinceDebugContainer;
		DropdownField _provinceCountryDropdown;
		Button _btnChangeProvinceOwner;
		Button _btnChangeProvinceOccupation;
		Button _btnResetProvinceOccupation;
		readonly List<string> _provinceDropdownCountryIds = new();
		string _lastProvinceIdForDropdown = "";
		readonly List<Button> _selectedCountryCharacterDebugButtons = new();
		VisualElement _relationDebugContainer;
		DropdownField _relationCountryDropdown;
		Button _btnSetCountryFriend;
		Button _btnSetCountryRival;
		Button _btnClearCountryRelation;
		readonly List<string> _relationDropdownCountryIds = new();
		DebugCardAvailabilityView _myOrgCardDebug;
		DebugCardAvailabilityView _selectedOrgCardDebug;
		// Show the raw (un-animated) Actual value alongside the debug hand/deck lists so it
		// can be compared against the animated HUD counter to catch it getting stuck on a
		// stale, animation-barrier-held value (see StateEquality.ResourceStateEntryEquals).
		Label _myOrgRawGoldLabel;
		Label _selectedOrgRawGoldLabel;
		readonly LeaderboardState _debugLeaderboard = new LeaderboardState();
		readonly PullRefreshTimer _debugPullTimer = new PullRefreshTimer();
		readonly OrgCardAvailabilityState _myOrgCardAvailability = new OrgCardAvailabilityState();
		readonly OrgCardAvailabilityState _selectedOrgCardAvailability = new OrgCardAvailabilityState();
		bool _myOrgDeckOpen;
		bool _myOrgHandOpen;
		bool _selectedOrgDeckOpen;
		bool _selectedOrgHandOpen;
		Label _selectedCountryDebugName;
		Label _selectedOrgDebugName;
		VisualElement _controlOrgDebugList;
		VisualElement _controlOrgDebugContainer;
		DropdownField _controlOrgDropdown;
		Button _btnControlOrgPlus;
		Button _btnControlOrgMinus;
		readonly List<string> _controlOrgDropdownOrgIds = new();
		List<WinConditionHintRowState> _winConditionRows = new();
		int _lastOrgAgentSlotCount = -1;

		[Inject]
		void Construct(
			VisualState state,
			IWriteOnlyCommandAccessor commands,
			ILocalization loc,
			CharacterConfig characterConfig,
			ActionConfig actionConfig,
			CountryConfig countryConfig,
			GameSettings gameSettings,
			GameLogic gameLogic) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_characterConfig = characterConfig;
			_actionConfig = actionConfig;
			_countryConfig = countryConfig;
			_gameSettings = gameSettings;
			_gameLogic = gameLogic;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			_root = _document.rootVisualElement;
		}

		void Start() {
			var root = _root;

			_btnDebugToggle = root.Q<Button>("btn-debug-toggle");
			_debugPanel = root.Q("debug-panel");
			_btnSelectedCountryDebugMenu = root.Q<Button>("btn-selected-country-debug-menu");
			_selectedCountryDebugMenu = root.Q("selected-country-debug-menu");
			_btnMyOrgDebugMenu = root.Q<Button>("btn-my-org-debug-menu");
			_myOrgDebugMenu = root.Q("my-org-debug-menu");
			_btnSelectedOrgDebugMenu = root.Q<Button>("btn-selected-org-debug-menu");
			_selectedOrgDebugMenu = root.Q("selected-org-debug-menu");
			_selectedCountryDebugName = root.Q<Label>("selected-country-debug-name");
			_selectedOrgDebugName = root.Q<Label>("selected-org-debug-name");
			_btnEcsViewer = root.Q<Button>("btn-ecs-viewer");

			_btnDebugToggle.OnClick(ToggleDebugPanel);
			_btnEcsViewer.OnClick(OpenEcsViewer);

			_btnFpsToggle = root.Q<Button>("btn-fps-toggle");
			_fpsLabel = root.Q<Label>("fps-label");
			if (_btnFpsToggle != null) {
				_btnFpsToggle.OnClick(ToggleFpsDisplay);
			}
			SetFpsEnabled(false);
			RegisterDebugMenuToggle(_btnSelectedCountryDebugMenu, _selectedCountryDebugMenu, "Selected country");
			RegisterDebugMenuToggle(_btnMyOrgDebugMenu, _myOrgDebugMenu, "My org");
			RegisterDebugMenuToggle(_btnSelectedOrgDebugMenu, _selectedOrgDebugMenu, "Selected org");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-characters"), root.Q("selected-country-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-relations"), root.Q("selected-country-relations"), "Relations");
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-characters"), root.Q("my-org-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-deck"), root.Q("my-org-deck"), "Deck",
				open => { _myOrgDeckOpen = open; _debugPullTimer.RequestImmediate(); RefreshMyOrgCardAvailability(); });
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-hand"), root.Q("my-org-hand"), "Hand",
				open => { _myOrgHandOpen = open; _debugPullTimer.RequestImmediate(); RefreshMyOrgCardAvailability(); });
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-deck"), root.Q("selected-org-deck"), "Deck",
				open => { _selectedOrgDeckOpen = open; _debugPullTimer.RequestImmediate(); RefreshSelectedOrgCardAvailability(); });
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-hand"), root.Q("selected-org-hand"), "Hand",
				open => { _selectedOrgHandOpen = open; _debugPullTimer.RequestImmediate(); RefreshSelectedOrgCardAvailability(); });
#if UNITY_WEBGL && !UNITY_EDITOR
			_btnEcsViewer.style.display = DisplayStyle.None;
#endif

			var btnGoldPlus  = root.Q<Button>("btn-gold-plus");
			var btnGoldMinus = root.Q<Button>("btn-gold-minus");
			if (btnGoldPlus != null) {
				btnGoldPlus.OnClick(() => PushChangeGoldCommand(+1000));
			}
			if (btnGoldMinus != null) {
				btnGoldMinus.OnClick(() => PushChangeGoldCommand(-1000));
			}

			var btnSelectedOrgGoldPlus  = root.Q<Button>("btn-selected-org-gold-plus");
			var btnSelectedOrgGoldMinus = root.Q<Button>("btn-selected-org-gold-minus");
			if (btnSelectedOrgGoldPlus != null) {
				btnSelectedOrgGoldPlus.OnClick(() => PushSelectedOrgChangeGoldCommand(+1000));
			}
			if (btnSelectedOrgGoldMinus != null) {
				btnSelectedOrgGoldMinus.OnClick(() => PushSelectedOrgChangeGoldCommand(-1000));
			}

			var btnSelectedOrgDestroy = root.Q<Button>("btn-selected-org-destroy");
			if (btnSelectedOrgDestroy != null) {
				btnSelectedOrgDestroy.OnClick(() => PushForceOrgDestroyCommand(GetSelectedOrgId()));
			}

			// Country character debug buttons
			var characterDebugContainer = root.Q("character-debug-container");
			if (characterDebugContainer != null) {
				if (_characterConfig != null) {
					foreach (var role in _characterConfig.Roles) {
						bool usedInCountryPool = false;
						foreach (var cp in _characterConfig.CountryPools) {
							if (cp.Slots.ContainsKey(role.RoleId)) { usedInCountryPool = true; break; }
						}
						if (!usedInCountryPool) { continue; }
						string capturedRoleId = role.RoleId;
						var nextBtn = new Button(() => PushCycleCharacter(_state?.SelectedCountry?.CountryId ?? "", capturedRoleId, 0));
						nextBtn.text = $"Next: {role.RoleId}";
						nextBtn.AddToClassList("gs-btn");
						nextBtn.AddToClassList("gs-btn--small");
						nextBtn.AddToClassList("debug-panel-button");
						characterDebugContainer.Add(nextBtn);
						_selectedCountryCharacterDebugButtons.Add(nextBtn);

						var dropBtn = new Button(() => PushDropCharacter(_state?.SelectedCountry?.CountryId ?? "", capturedRoleId, 0));
						dropBtn.text = $"Drop: {role.RoleId}";
						dropBtn.AddToClassList("gs-btn");
						dropBtn.AddToClassList("gs-btn--small");
						dropBtn.AddToClassList("debug-panel-button");
						characterDebugContainer.Add(dropBtn);
						_selectedCountryCharacterDebugButtons.Add(dropBtn);
					}

					var improveOpinionBtn = new Button(() => PushImproveOpinionCommand(_state?.SelectedCountry?.CountryId ?? ""));
					improveOpinionBtn.text = "Improve Opinion";
					improveOpinionBtn.AddToClassList("gs-btn");
					improveOpinionBtn.AddToClassList("gs-btn--small");
					improveOpinionBtn.AddToClassList("debug-panel-button");
					characterDebugContainer.Add(improveOpinionBtn);
				}
			}

			_btnSelectedProvinceDebugMenu = root.Q<Button>("btn-selected-province-debug-menu");
			_selectedProvinceDebugMenu = root.Q("selected-province-debug-menu");
			RegisterDebugMenuToggle(_btnSelectedProvinceDebugMenu, _selectedProvinceDebugMenu, "Selected province");
			BuildProvinceDebugUi();
			BuildRelationDebugUi();
			BuildControlOrgDebugUi();

			// My org: player org's full card availability (org cards + country cards), always
			// shown regardless of map selection; keeps the existing draw/discard debug cheats.
			_myOrgCardDebug = new DebugCardAvailabilityView(
				root.Q("my-org-deck"),
				root.Q("my-org-hand"),
				_loc,
				_actionConfig,
				PushDebugDrawCountryCardCommand,
				PushDebugDiscardCountryCardCommand);

			// Selected org: whichever org is dominant for the selected country under the org
			// lens. Read-only - the draw/discard debug cheats are only wired for the player's org.
			_selectedOrgCardDebug = new DebugCardAvailabilityView(
				root.Q("selected-org-deck"),
				root.Q("selected-org-hand"),
				_loc,
				_actionConfig);
			_myOrgRawGoldLabel = root.Q<Label>("my-org-raw-gold");
			_selectedOrgRawGoldLabel = root.Q<Label>("selected-org-raw-gold");

			int availableCountryCount = _countryConfig != null ? CountAvailableCountries(_countryConfig) : 0;
			var (_, _, winConditionRows) = WinConditionHintProjector.Build(_gameSettings?.CompletionCondition, availableCountryCount);
			_winConditionRows = winConditionRows;

			RebuildOrgCharDebugButtons();
			RefreshSelectedCountryCharacterDebugButtons();
			RefreshSelectedProvinceDebugMenu();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
			RefreshMyOrgCardAvailability();
			RefreshSelectedOrgCardAvailability();
			RefreshSelectedCountryDebugName();
			RefreshSelectedOrgDebugName();
			RefreshSelectedOrgDebugMenuAvailability();
			RefreshDebugLeaderboardCache();
			RebuildControlOrgDropdown();
			RefreshControlOrgDebugList();
		}

		void OnEnable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged += HandleCountryChanged;
			_state.PlayerOrganization.Resources.PropertyChanged += HandlePlayerResourcesChanged;
			_state.PlayerOrganization.Characters.PropertyChanged += HandleOrgCharactersChanged;
			_state.SelectedCountry.Control.PropertyChanged += HandleControlChanged;
			_state.SelectedCountry.Relations.PropertyChanged += HandleRelationsChanged;
			_state.OrgLensOrganizationResources.PropertyChanged += HandleOrgLensResourcesChanged;
			_state.SelectedProvince.PropertyChanged += HandleSelectedProvinceChanged;
			_state.ProvinceOwnership.PropertyChanged += HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged += HandleProvinceOccupationChanged;
			_state.MapLens.PropertyChanged += HandleLensChanged;

			RefreshSelectedCountryCharacterDebugButtons();
			RefreshSelectedOrgDebugMenuAvailability();
			RefreshDebugLeaderboardCache();
			RebuildControlOrgDropdown();
			RefreshSelectedProvinceDebugMenu();
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.SelectedCountry.PropertyChanged -= HandleCountryChanged;
			_state.PlayerOrganization.Resources.PropertyChanged -= HandlePlayerResourcesChanged;
			_state.PlayerOrganization.Characters.PropertyChanged -= HandleOrgCharactersChanged;
			_state.SelectedCountry.Control.PropertyChanged -= HandleControlChanged;
			_state.SelectedCountry.Relations.PropertyChanged -= HandleRelationsChanged;
			_state.OrgLensOrganizationResources.PropertyChanged -= HandleOrgLensResourcesChanged;
			_state.SelectedProvince.PropertyChanged -= HandleSelectedProvinceChanged;
			_state.ProvinceOwnership.PropertyChanged -= HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged -= HandleProvinceOccupationChanged;
			_state.MapLens.PropertyChanged -= HandleLensChanged;
			_lastOrgAgentSlotCount = -1;
		}

		void Update() {
			if (_fpsEnabled) {
				UpdateFpsCounter();
			}
			if (_debugPanelOpen && _debugPullTimer.ShouldRefresh(Time.deltaTime, _state != null && _state.Time.IsPaused)) {
				RefreshDebugPulledState();
			}
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshSelectedCountryCharacterDebugButtons();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
			RefreshSelectedCountryDebugName();
			RefreshControlOrgDebugList();
		}

		void HandlePlayerResourcesChanged(object sender, PropertyChangedEventArgs e) {
			RefreshMyOrgCardAvailability();
		}

		void HandleControlChanged(object sender, PropertyChangedEventArgs e) {
			RefreshControlOrgDebugList();
		}

		void HandleRelationsChanged(object sender, PropertyChangedEventArgs e) {
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
		}

		void HandleOrgLensResourcesChanged(object sender, PropertyChangedEventArgs e) {
			RefreshSelectedOrgCardAvailability();
			RefreshSelectedOrgDebugName();
			RefreshSelectedOrgDebugMenuAvailability();
		}

		void HandleSelectedProvinceChanged(object sender, PropertyChangedEventArgs e) {
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleProvinceOwnershipChanged(object sender, PropertyChangedEventArgs e) {
			_lastProvinceIdForDropdown = "";
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleProvinceOccupationChanged(object sender, PropertyChangedEventArgs e) {
			RefreshProvinceActionButtons();
		}

		void HandleLensChanged(object sender, PropertyChangedEventArgs e) {
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleOrgCharactersChanged(object sender, PropertyChangedEventArgs e) {
			int agentCount = 0;
			if (_state?.PlayerOrganization?.Characters?.Slots != null) {
				foreach (var slot in _state.PlayerOrganization.Characters.Slots) {
					if (slot.RoleId == "agent") { agentCount++; }
				}
			}
			if (agentCount == _lastOrgAgentSlotCount) { return; }
			_lastOrgAgentSlotCount = agentCount;
			RebuildOrgCharDebugButtons();
		}

		void BuildProvinceDebugUi() {
			_provinceDebugContainer = _root.Q("province-debug-container");
			if (_provinceDebugContainer == null) { return; }

			_provinceCountryDropdown = new DropdownField();
			_provinceCountryDropdown.AddToClassList("debug-panel-button");
			_provinceCountryDropdown.RegisterValueChangedCallback(_ => RefreshProvinceActionButtons());
			_provinceDebugContainer.Add(_provinceCountryDropdown);

			_btnChangeProvinceOwner = new Button { text = "Change owner" };
			_btnChangeProvinceOwner.AddToClassList("gs-btn");
			_btnChangeProvinceOwner.AddToClassList("gs-btn--small");
			_btnChangeProvinceOwner.AddToClassList("debug-panel-button");
			_btnChangeProvinceOwner.OnClick(PushChangeProvinceOwnerCommand);
			_provinceDebugContainer.Add(_btnChangeProvinceOwner);

			_btnChangeProvinceOccupation = new Button { text = "Change occupation" };
			_btnChangeProvinceOccupation.AddToClassList("gs-btn");
			_btnChangeProvinceOccupation.AddToClassList("gs-btn--small");
			_btnChangeProvinceOccupation.AddToClassList("debug-panel-button");
			_btnChangeProvinceOccupation.OnClick(PushSetProvinceOccupationCommand);
			_provinceDebugContainer.Add(_btnChangeProvinceOccupation);

			_btnResetProvinceOccupation = new Button { text = "Reset occupation" };
			_btnResetProvinceOccupation.AddToClassList("gs-btn");
			_btnResetProvinceOccupation.AddToClassList("gs-btn--small");
			_btnResetProvinceOccupation.AddToClassList("debug-panel-button");
			_btnResetProvinceOccupation.OnClick(PushClearProvinceOccupationCommand);
			_provinceDebugContainer.Add(_btnResetProvinceOccupation);
		}

		void BuildRelationDebugUi() {
			_relationDebugContainer = _root.Q("relation-debug-container");
			if (_relationDebugContainer == null) { return; }

			_relationCountryDropdown = new DropdownField();
			_relationCountryDropdown.AddToClassList("debug-panel-button");
			_relationCountryDropdown.RegisterValueChangedCallback(_ => RefreshRelationActionButtons());
			_relationDebugContainer.Add(_relationCountryDropdown);

			_btnSetCountryFriend = new Button(() => PushSetCountryRelationCommand(RelationKind.Friend)) { text = "Set friend" };
			_btnSetCountryFriend.AddToClassList("gs-btn");
			_btnSetCountryFriend.AddToClassList("gs-btn--small");
			_btnSetCountryFriend.AddToClassList("debug-panel-button");
			_relationDebugContainer.Add(_btnSetCountryFriend);

			_btnSetCountryRival = new Button(() => PushSetCountryRelationCommand(RelationKind.Rival)) { text = "Set rival" };
			_btnSetCountryRival.AddToClassList("gs-btn");
			_btnSetCountryRival.AddToClassList("gs-btn--small");
			_btnSetCountryRival.AddToClassList("debug-panel-button");
			_relationDebugContainer.Add(_btnSetCountryRival);

			_btnClearCountryRelation = new Button(PushClearCountryRelationCommand) { text = "Clear relation" };
			_btnClearCountryRelation.AddToClassList("gs-btn");
			_btnClearCountryRelation.AddToClassList("gs-btn--small");
			_btnClearCountryRelation.AddToClassList("debug-panel-button");
			_relationDebugContainer.Add(_btnClearCountryRelation);
		}

		void BuildControlOrgDebugUi() {
			_controlOrgDebugList = _root.Q("control-org-debug-list");
			_controlOrgDebugContainer = _root.Q("control-org-debug-container");
			if (_controlOrgDebugContainer == null) { return; }

			_controlOrgDropdown = new DropdownField();
			_controlOrgDropdown.AddToClassList("debug-panel-button");
			_controlOrgDebugContainer.Add(_controlOrgDropdown);

			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			_controlOrgDebugContainer.Add(row);

			_btnControlOrgPlus = new Button { text = "Control+10" };
			_btnControlOrgPlus.AddToClassList("gs-btn");
			_btnControlOrgPlus.AddToClassList("gs-btn--small");
			_btnControlOrgPlus.AddToClassList("debug-panel-button");
			_btnControlOrgPlus.OnClick(() => PushControlOrgCommand(+10));
			row.Add(_btnControlOrgPlus);

			_btnControlOrgMinus = new Button { text = "Control-10" };
			_btnControlOrgMinus.AddToClassList("gs-btn");
			_btnControlOrgMinus.AddToClassList("gs-btn--small");
			_btnControlOrgMinus.AddToClassList("debug-panel-button");
			_btnControlOrgMinus.OnClick(() => PushControlOrgCommand(-10));
			row.Add(_btnControlOrgMinus);
		}

		void ToggleDebugPanel() {
			_debugPanelOpen = !_debugPanelOpen;
			_debugPanel.style.display = _debugPanelOpen ? DisplayStyle.Flex : DisplayStyle.None;
			if (_debugPanelOpen) {
				_debugPullTimer.RequestImmediate();
				RefreshDebugPulledState();
			}
		}

		// Cold-panel pull model (Docs/Specs/26_08_28_16_ui-refactoring phase 2): the debug panel's
		// leaderboard-derived control-org dropdown and the two org card availability listings are
		// only ever read while the debug panel is open, so they project on open, then on the
		// PullRefreshTimer's cadence from Update, instead of every tick regardless of visibility.
		void RefreshDebugPulledState() {
			RefreshDebugLeaderboardCache();
			RebuildControlOrgDropdown();
			RefreshMyOrgCardAvailability();
			RefreshSelectedOrgCardAvailability();
		}

		void RefreshDebugLeaderboardCache() {
			if (_gameLogic == null) {
				return;
			}
			LeaderboardProjector.Project(_gameLogic.World, _debugLeaderboard, _gameLogic.Resources, _countryConfig);
		}

		void ToggleFpsDisplay() {
			SetFpsEnabled(!_fpsEnabled);
		}

		void SetFpsEnabled(bool isEnabled) {
			_fpsEnabled = isEnabled;
			if (_btnFpsToggle != null) {
				_btnFpsToggle.RemoveFromClassList(isEnabled ? "gs-toggle-off" : "gs-toggle-on");
				_btnFpsToggle.AddToClassList(isEnabled ? "gs-toggle-on" : "gs-toggle-off");
			}
			if (_fpsLabel != null) {
				_fpsLabel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (!isEnabled) {
				_fpsFrameTimestamps.Clear();
			}
		}

		void UpdateFpsCounter() {
			float now = Time.unscaledTime;
			_fpsFrameTimestamps.Enqueue(now);
			while (_fpsFrameTimestamps.Count > 1 && now - _fpsFrameTimestamps.Peek() > 1f) {
				_fpsFrameTimestamps.Dequeue();
			}
			float windowDuration = now - _fpsFrameTimestamps.Peek();
			int fps = windowDuration > 0f ? Mathf.CeilToInt(_fpsFrameTimestamps.Count / windowDuration) : 0;
			if (_fpsLabel != null) {
				_fpsLabel.text = $"FPS: {fps}";
			}
		}

		void RegisterDebugMenuToggle(Button button, VisualElement menu, string label, System.Action<bool> onToggled = null) {
			if (button == null || menu == null) {
				return;
			}

			menu.style.display = DisplayStyle.None;
			button.text = $"> {label}";
			button.OnClick(() => {
				bool isOpen = menu.style.display != DisplayStyle.None;
				bool willOpen = !isOpen;
				menu.style.display = willOpen ? DisplayStyle.Flex : DisplayStyle.None;
				button.text = $"{(willOpen ? "v" : ">")} {label}";
				onToggled?.Invoke(willOpen);
			});
		}

		void OpenEcsViewer() {
			var url = EcsViewerBridge.CurrentUrl;
			if (url == null) {
				Debug.LogWarning("[DebugPanelDocument] ECS Viewer URL is not available — is the bridge running?");
				return;
			}
			Application.OpenURL(url);
		}

		void RefreshMyOrgCardAvailability() {
			if (_state == null || _myOrgCardDebug == null || _gameLogic == null) {
				return;
			}
			string myOrgId = _state.PlayerOrganization.IsValid ? _state.PlayerOrganization.OrgId : "";
			DebugOrgCardAvailabilityProjector.Project(
				_gameLogic.World, _myOrgCardAvailability, myOrgId, _state.Time.CurrentTime,
				_myOrgDeckOpen, _myOrgHandOpen,
				_gameLogic.ActionConfig, _gameLogic.EffectConfig, _gameLogic.Resources, _gameLogic.Relations,
				_gameLogic.HqCountryByOrgId, _gameLogic.MaxControlPool);
			_myOrgCardDebug.RefreshDeck(_myOrgCardAvailability.Deck);
			_myOrgCardDebug.RefreshHand(_myOrgCardAvailability.Hand, GetPlayerGold());
			if (_myOrgRawGoldLabel != null) {
				double display = GetPlayerGold();
				double raw = GetPlayerGoldRaw();
				_myOrgRawGoldLabel.text = $"Raw gold: {raw:0.##}";
				if (System.Math.Abs(display - raw) > 0.01) {
					Debug.Log($"[GOLD-DEBUG] HUD/raw gold MISMATCH: display={display:0.##} raw={raw:0.##} diff={display - raw:0.##}");
				}
			}
		}

		void RefreshSelectedOrgCardAvailability() {
			if (_state == null || _selectedOrgCardDebug == null || _gameLogic == null) {
				return;
			}
			string selectedOrgId = DebugOrgCardAvailabilityProjector.ResolveSelectedOrgId(
				_state.MapLens.Lens, _state.OrgLensOrganizationResources.IsValid, _state.OrgLensOrganizationResources.CountryId);
			DebugOrgCardAvailabilityProjector.Project(
				_gameLogic.World, _selectedOrgCardAvailability, selectedOrgId, _state.Time.CurrentTime,
				_selectedOrgDeckOpen, _selectedOrgHandOpen,
				_gameLogic.ActionConfig, _gameLogic.EffectConfig, _gameLogic.Resources, _gameLogic.Relations,
				_gameLogic.HqCountryByOrgId, _gameLogic.MaxControlPool);
			_selectedOrgCardDebug.RefreshDeck(_selectedOrgCardAvailability.Deck);
			_selectedOrgCardDebug.RefreshHand(_selectedOrgCardAvailability.Hand, GetOrgLensGold());
			if (_selectedOrgRawGoldLabel != null) {
				_selectedOrgRawGoldLabel.text = $"Raw gold: {GetOrgLensGoldRaw():0.##}";
			}
		}

		double GetOrgLensGold() {
			if (_state?.OrgLensOrganizationResources?.Resources == null) {
				return 0;
			}
			foreach (var resource in _state.OrgLensOrganizationResources.Resources) {
				if (resource.ResourceId == "gold") {
					return resource.Value.Display;
				}
			}
			return 0;
		}

		// Actual (not Display): bypasses animation barriers, for comparing against the animated
		// HUD counter in the debug menu.
		double GetOrgLensGoldRaw() {
			if (_state?.OrgLensOrganizationResources?.Resources == null) {
				return 0;
			}
			foreach (var resource in _state.OrgLensOrganizationResources.Resources) {
				if (resource.ResourceId == "gold") {
					return resource.Value.Actual;
				}
			}
			return 0;
		}

		void PushDebugDrawCountryCardCommand(string actionId, string targetCountryId) {
			if (_state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) {
				return;
			}
			if (string.IsNullOrEmpty(actionId)) {
				return;
			}
			_commands.Push(new DebugDrawCardCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				CountryId = _state.SelectedCountry.CountryId,
				ActionId = actionId,
				TargetCountryId = targetCountryId ?? ""
			});
		}

		void PushDebugDiscardCountryCardCommand(string actionId, string targetCountryId, int slotIndex) {
			if (_state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) {
				return;
			}
			if (string.IsNullOrEmpty(actionId)) {
				return;
			}
			_commands.Push(new DebugDiscardCardCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				CountryId = _state.SelectedCountry.CountryId,
				ActionId = actionId,
				TargetCountryId = targetCountryId ?? "",
				SlotIndex = slotIndex
			});
		}

		double GetPlayerGold() {
			if (_state?.PlayerOrganization?.Resources?.Resources == null) {
				return 0;
			}
			foreach (var resource in _state.PlayerOrganization.Resources.Resources) {
				if (resource.ResourceId == "gold") {
					return resource.Value.Display;
				}
			}
			return 0;
		}

		// Actual (not Display): bypasses animation barriers, for comparing against the animated
		// HUD counter in the debug menu.
		double GetPlayerGoldRaw() {
			if (_state?.PlayerOrganization?.Resources?.Resources == null) {
				return 0;
			}
			foreach (var resource in _state.PlayerOrganization.Resources.Resources) {
				if (resource.ResourceId == "gold") {
					return resource.Value.Actual;
				}
			}
			return 0;
		}

		void RefreshSelectedCountryDebugName() {
			if (_selectedCountryDebugName == null) { return; }
			_selectedCountryDebugName.text = _state != null && _state.SelectedCountry.IsValid
				? GetCountryDisplayName(_state.SelectedCountry.CountryId)
				: "";
		}

		void RefreshSelectedOrgDebugName() {
			if (_selectedOrgDebugName == null) { return; }
			string orgId = GetSelectedOrgId();
			_selectedOrgDebugName.text = string.IsNullOrEmpty(orgId) ? "" : GetOrgDisplayName(orgId);
		}

		string GetOrgDisplayName(string orgId) {
			if (string.IsNullOrEmpty(orgId)) { return ""; }
			if (_state?.SelectedCountry?.Control?.OrgEntries != null) {
				foreach (var org in _state.SelectedCountry.Control.OrgEntries) {
					if (org.OrgId == orgId) { return org.DisplayName; }
				}
			}
			if (_debugLeaderboard?.Organizations != null) {
				foreach (var entry in _debugLeaderboard.Organizations) {
					if (entry.EntityId == orgId) { return entry.DisplayName; }
				}
			}
			return orgId;
		}

		void RebuildControlOrgDropdown() {
			if (_controlOrgDropdown == null || _state == null) { return; }
			// Preserve the selected org by id (not index) - Leaderboard reorders/rebuilds on
			// almost every tick (scores change constantly), so clamping by index alone would
			// silently reselect a different org whenever the sort order shifts.
			string previouslySelectedOrgId = GetSelectedControlOrgDropdownOrgId();
			_controlOrgDropdownOrgIds.Clear();
			var choices = new List<string>();
			foreach (var entry in _debugLeaderboard.Organizations) {
				_controlOrgDropdownOrgIds.Add(entry.EntityId);
				choices.Add(entry.DisplayName);
			}
			_controlOrgDropdown.choices = choices;
			int restoredIndex = _controlOrgDropdownOrgIds.IndexOf(previouslySelectedOrgId);
			_controlOrgDropdown.index = restoredIndex >= 0 ? restoredIndex : (choices.Count > 0 ? 0 : -1);
		}

		void RefreshControlOrgDebugList() {
			if (_controlOrgDebugList == null) { return; }
			_controlOrgDebugList.Clear();
			if (_state == null || !_state.SelectedCountry.IsValid) { return; }
			foreach (var org in _state.SelectedCountry.Control.OrgEntries) {
				var label = new Label($"{org.DisplayName}: {org.Control}");
				label.AddToClassList("gs-label");
				label.AddToClassList("debug-panel-button");
				_controlOrgDebugList.Add(label);
			}
		}

		string GetSelectedControlOrgDropdownOrgId() {
			if (_controlOrgDropdown == null) { return ""; }
			int index = _controlOrgDropdown.index;
			return index >= 0 && index < _controlOrgDropdownOrgIds.Count ? _controlOrgDropdownOrgIds[index] : "";
		}

		void PushControlOrgCommand(int delta) {
			if (_state == null || _commands == null || !_state.SelectedCountry.IsValid) { return; }
			string orgId = GetSelectedControlOrgDropdownOrgId();
			if (string.IsNullOrEmpty(orgId)) { return; }
			// Positive deltas grow the org's "permanent_" control effect in this country;
			// negative deltas drain every control effect the org has there (including its
			// "base_" HQ-seed effect) via GameLogic.ApplyChangeControl, so this can zero an
			// org's control even on its own HQ - see ControlQuery.ReduceOrgControlInCountry.
			_commands.Push(new ChangeControlCommand {
				OrgId = orgId,
				CountryId = _state.SelectedCountry.CountryId,
				Delta = delta
			});
		}

		void PushChangeGoldCommand(double amount) {
			if (_state == null || !_state.PlayerOrganization.IsValid) { return; }
			_commands.Push(new GS.Game.Commands.DebugChangeGoldCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				Amount = amount
			});
		}

		string GetSelectedOrgId() =>
			_state != null && _state.OrgLensOrganizationResources.IsValid
				? _state.OrgLensOrganizationResources.CountryId
				: "";

		void PushSelectedOrgChangeGoldCommand(double amount) {
			if (_state == null || !_state.OrgLensOrganizationResources.IsValid) { return; }
			_commands.Push(new GS.Game.Commands.DebugChangeGoldCommand {
				OrgId = _state.OrgLensOrganizationResources.CountryId,
				Amount = amount
			});
		}

		void PushForceOrgDestroyCommand(string targetOrgId) {
			if (string.IsNullOrEmpty(targetOrgId) || _commands == null) { return; }
			_commands.Push(new GS.Game.Commands.DebugForceOrgDestroyCommand { TargetOrgId = targetOrgId });
		}

		void RefreshSelectedCountryCharacterDebugButtons() {
			bool countrySelected = _state != null && _state.SelectedCountry.IsValid;
			if (_btnSelectedCountryDebugMenu != null) {
				_btnSelectedCountryDebugMenu.SetEnabled(countrySelected);
				if (!countrySelected) {
					if (_selectedCountryDebugMenu != null) {
						_selectedCountryDebugMenu.style.display = DisplayStyle.None;
					}
					_btnSelectedCountryDebugMenu.text = "> Selected country";
				}
			}
			foreach (var button in _selectedCountryCharacterDebugButtons) {
				button.SetEnabled(countrySelected);
			}
		}

		void RefreshSelectedOrgDebugMenuAvailability() {
			bool orgSelected = !string.IsNullOrEmpty(GetSelectedOrgId());
			if (_btnSelectedOrgDebugMenu == null) { return; }
			_btnSelectedOrgDebugMenu.SetEnabled(orgSelected);
			if (!orgSelected) {
				if (_selectedOrgDebugMenu != null) {
					_selectedOrgDebugMenu.style.display = DisplayStyle.None;
				}
				_btnSelectedOrgDebugMenu.text = "> Selected org";
			}
		}

		void RefreshSelectedProvinceDebugMenu() {
			if (_state == null) { return; }
			bool valid = _state.MapLens.Lens == MapLens.Province && _state.SelectedProvince.IsValid;
			if (_btnSelectedProvinceDebugMenu != null) {
				_btnSelectedProvinceDebugMenu.SetEnabled(valid);
				if (!valid) {
					if (_selectedProvinceDebugMenu != null) {
						_selectedProvinceDebugMenu.style.display = DisplayStyle.None;
					}
					_btnSelectedProvinceDebugMenu.text = "> Selected province";
				}
			}
			if (!valid) {
				_lastProvinceIdForDropdown = "";
				return;
			}

			string provinceId = _state.SelectedProvince.ProvinceId;
			if (provinceId != _lastProvinceIdForDropdown) {
				RebuildProvinceCountryDropdown(GetProvinceOwner(provinceId), _state.PlayerOrganization.HqCountryId);
				_lastProvinceIdForDropdown = provinceId;
			}
			RefreshProvinceActionButtons();
		}

		void RebuildProvinceCountryDropdown(string ownerId, string hqCountryId) {
			if (_provinceCountryDropdown == null || _countryConfig == null) { return; }
			_provinceDropdownCountryIds.Clear();
			var choices = new List<string>();
			void AddCountry(string countryId) {
				if (string.IsNullOrEmpty(countryId) || _provinceDropdownCountryIds.Contains(countryId)) { return; }
				_provinceDropdownCountryIds.Add(countryId);
				choices.Add(GetCountryDisplayName(countryId));
			}
			AddCountry(ownerId);
			AddCountry(hqCountryId);
			foreach (var entry in _countryConfig.Countries) {
				if (entry.IsAvailable) { AddCountry(entry.CountryId); }
			}
			_provinceCountryDropdown.choices = choices;
			_provinceCountryDropdown.index = choices.Count > 0 ? 0 : -1;
		}

		void RefreshProvinceActionButtons() {
			if (_state == null || _provinceCountryDropdown == null) { return; }
			string provinceId = _state.SelectedProvince.ProvinceId;
			string ownerId = GetProvinceOwner(provinceId);
			string selectedCountryId = GetSelectedProvinceDropdownCountryId();
			bool differsFromOwner = !string.IsNullOrEmpty(selectedCountryId) && selectedCountryId != ownerId;
			_btnChangeProvinceOwner?.SetEnabled(differsFromOwner);
			_btnChangeProvinceOccupation?.SetEnabled(differsFromOwner);
			bool occupied = !string.IsNullOrEmpty(GetProvinceOccupier(provinceId));
			_btnResetProvinceOccupation?.SetEnabled(occupied);
		}

		string GetSelectedProvinceDropdownCountryId() {
			if (_provinceCountryDropdown == null) { return ""; }
			int index = _provinceCountryDropdown.index;
			return index >= 0 && index < _provinceDropdownCountryIds.Count ? _provinceDropdownCountryIds[index] : "";
		}

		void RebuildRelationCountryDropdown() {
			if (_relationCountryDropdown == null || _countryConfig == null || _state == null) { return; }
			_relationDropdownCountryIds.Clear();
			var choices = new List<string>();
			string selectedCountryId = _state.SelectedCountry.CountryId;
			var friends = _state.SelectedCountry.Relations.Friends;
			var rivals = _state.SelectedCountry.Relations.Rivals;
			foreach (var entry in _countryConfig.Countries) {
				if (!entry.IsAvailable || entry.CountryId == selectedCountryId) { continue; }
				_relationDropdownCountryIds.Add(entry.CountryId);
				string suffix = "";
				if (ContainsCountry(friends, entry.CountryId)) {
					suffix = " (Friend)";
				} else if (ContainsCountry(rivals, entry.CountryId)) {
					suffix = " (Rival)";
				}
				choices.Add(GetCountryDisplayName(entry.CountryId) + suffix);
			}
			_relationCountryDropdown.choices = choices;
			_relationCountryDropdown.index = choices.Count > 0 ? 0 : -1;
		}

		static bool ContainsCountry(IReadOnlyList<string> countryIds, string countryId) {
			for (int i = 0; i < countryIds.Count; i++) {
				if (countryIds[i] == countryId) { return true; }
			}
			return false;
		}

		void RefreshRelationActionButtons() {
			bool countrySelected = _state != null && _state.SelectedCountry.IsValid;
			bool hasTarget = countrySelected && _relationCountryDropdown != null && _relationCountryDropdown.index >= 0;
			_btnSetCountryFriend?.SetEnabled(hasTarget);
			_btnSetCountryRival?.SetEnabled(hasTarget);
			_btnClearCountryRelation?.SetEnabled(hasTarget);
		}

		string GetSelectedRelationDropdownCountryId() {
			if (_relationCountryDropdown == null) { return ""; }
			int index = _relationCountryDropdown.index;
			return index >= 0 && index < _relationDropdownCountryIds.Count ? _relationDropdownCountryIds[index] : "";
		}

		string GetProvinceOwner(string provinceId) {
			if (_state == null || string.IsNullOrEmpty(provinceId)) { return ""; }
			return _state.ProvinceOwnership.OwnerByProvinceId.TryGetValue(provinceId, out var ownerId) ? ownerId : "";
		}

		string GetProvinceOccupier(string provinceId) {
			if (_state == null || string.IsNullOrEmpty(provinceId)) { return ""; }
			return _state.ProvinceOccupation.OccupierByProvinceId.TryGetValue(provinceId, out var occupierId) ? occupierId : "";
		}

		string GetCountryDisplayName(string countryId) {
			string key = $"country_name.{countryId}";
			string name = _loc?.Get(key);
			return string.IsNullOrEmpty(name) || name == key ? countryId : name;
		}

		void PushChangeProvinceOwnerCommand() {
			if (_state == null || _commands == null) { return; }
			string provinceId = _state.SelectedProvince.ProvinceId;
			string countryId = GetSelectedProvinceDropdownCountryId();
			if (string.IsNullOrEmpty(provinceId) || string.IsNullOrEmpty(countryId)) { return; }
			_commands.Push(new DebugChangeProvinceOwnerCommand { ProvinceId = provinceId, NewOwnerId = countryId });
		}

		void PushSetProvinceOccupationCommand() {
			if (_state == null || _commands == null) { return; }
			string provinceId = _state.SelectedProvince.ProvinceId;
			string countryId = GetSelectedProvinceDropdownCountryId();
			if (string.IsNullOrEmpty(provinceId) || string.IsNullOrEmpty(countryId)) { return; }
			_commands.Push(new DebugSetProvinceOccupationCommand { ProvinceId = provinceId, OccupierId = countryId });
		}

		void PushClearProvinceOccupationCommand() {
			if (_state == null || _commands == null) { return; }
			string provinceId = _state.SelectedProvince.ProvinceId;
			if (string.IsNullOrEmpty(provinceId)) { return; }
			_commands.Push(new DebugClearProvinceOccupationCommand { ProvinceId = provinceId });
		}

		void PushSetCountryRelationCommand(RelationKind kind) {
			if (_state == null || _commands == null) { return; }
			string countryId = _state.SelectedCountry.CountryId;
			string otherCountryId = GetSelectedRelationDropdownCountryId();
			if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(otherCountryId)) { return; }
			_commands.Push(new DebugSetCountryRelationCommand { CountryIdA = countryId, CountryIdB = otherCountryId, Kind = kind });
		}

		void PushClearCountryRelationCommand() {
			if (_state == null || _commands == null) { return; }
			string countryId = _state.SelectedCountry.CountryId;
			string otherCountryId = GetSelectedRelationDropdownCountryId();
			if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(otherCountryId)) { return; }
			_commands.Push(new DebugClearCountryRelationCommand { CountryIdA = countryId, CountryIdB = otherCountryId });
		}

		static int CountAvailableCountries(CountryConfig countryConfig) {
			int count = 0;
			foreach (var entry in countryConfig.Countries) {
				if (entry.IsAvailable) { count++; }
			}
			return count;
		}

		static string FormatWinConditionLabel(WinConditionHintRowState row) {
			return row.Kind == WinConditionHintKind.TotalControl
				? $"{row.Value * 100:0}% control"
				: $"{(int)row.Value}/{row.AvailableCountryCount} countries";
		}

		string GetOpponentOrgId() {
			if (_debugLeaderboard?.Organizations == null) { return ""; }
			string playerOrgId = GetPlayerOrgId();
			string opponentOrgId = "";
			foreach (var entry in _debugLeaderboard.Organizations) {
				if (entry.EntityId == playerOrgId) { continue; }
				if (opponentOrgId == "" || string.CompareOrdinal(entry.EntityId, opponentOrgId) < 0) {
					opponentOrgId = entry.EntityId;
				}
			}
			return opponentOrgId;
		}

		void PushForceCompletionCondition(string targetOrgId, WinConditionHintRowState row) {
			if (string.IsNullOrEmpty(targetOrgId) || _commands == null) { return; }
			string conditionType = row.Kind == WinConditionHintKind.TotalControl ? "total_control" : "full_control_countries";
			_commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = targetOrgId,
				ConditionType = conditionType,
				Value = row.Value
			});
		}

		void RebuildOrgCharDebugButtons() {
			var orgCharDebugContainer = _root?.Q("org-char-debug-container");
			var orgMiscDebugContainer = _root?.Q("org-misc-debug-container");
			if (orgCharDebugContainer == null) { return; }
			orgCharDebugContainer.Clear();
			orgMiscDebugContainer?.Clear();

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
			if (_state?.PlayerOrganization?.Characters?.Slots != null) {
				foreach (var slot in _state.PlayerOrganization.Characters.Slots) {
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

			if (orgMiscDebugContainer == null) {
				return;
			}

			foreach (var row in _winConditionRows) {
				var capturedRow = row;
				string label = FormatWinConditionLabel(capturedRow);

				var winBtn = new Button();
				winBtn.text = $"Win ({label})";
				winBtn.AddToClassList("gs-btn");
				winBtn.AddToClassList("gs-btn--small");
				winBtn.AddToClassList("debug-panel-button");
				winBtn.OnClick(() => PushForceCompletionCondition(GetPlayerOrgId(), capturedRow));
				orgMiscDebugContainer.Add(winBtn);

				var loseBtn = new Button();
				loseBtn.text = $"Lose ({label})";
				loseBtn.AddToClassList("gs-btn");
				loseBtn.AddToClassList("gs-btn--small");
				loseBtn.AddToClassList("debug-panel-button");
				loseBtn.OnClick(() => PushForceCompletionCondition(GetOpponentOrgId(), capturedRow));
				orgMiscDebugContainer.Add(loseBtn);
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

		void PushImproveOpinionCommand(string countryId) {
			if (string.IsNullOrEmpty(countryId) || _commands == null) { return; }
			string orgId = _state?.PlayerOrganization?.OrgId ?? "";
			if (string.IsNullOrEmpty(orgId)) { return; }
			_commands.Push(new DebugImproveOpinionCommand { CountryId = countryId, OrgId = orgId });
		}
	}
}
