using System.Collections.Generic;
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Unity.EcsViewer;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class HUDDocument : MonoBehaviour {
		UIDocument _document;
		CountryInfoView _countryInfo;
		ProvinceInfoView _provinceInfo;
		PlayerOrgView _playerOrgView;
		TimeView _timeView;
		TooltipSystem _tooltip;
		VisualState _state;
		IWriteOnlyCommandAccessor _commands;
		ILocalization _loc;
		IFlyTextNotifier _flyText;
		long _lastNotifiedLogSequenceId = -1;
		ResourceConfig _resourceConfig;
		CharacterConfig _characterConfig;
		CharacterVisualConfig _characterVisualConfig;
		CountryVisualConfig _countryVisualConfig;
		OrgVisualConfig _orgVisualConfig;
		GameMenuDocument _gameMenu;
		LeaderboardWindowDocument _leaderboardWindow;
		GoalsWindowDocument _goalsWindow;
		WarProgressWindowDocument _warProgressWindow;
		WarIconsView _warIconsView;
		Button _btnMenu;
		Button _btnLeaderboard;
		Button _btnGoals;
		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnSelectedCountryDebugMenu;
		VisualElement _selectedCountryDebugMenu;
		Button _btnSelectedOrgDebugMenu;
		VisualElement _selectedOrgDebugMenu;
		Button _btnEcsViewer;
		VisualElement _controlDebugRow;
		bool _debugPanelOpen;
		OrgInfoDocument _orgInfoDocument;
		VisualElement _root;
		VisualElement _countryInfoRoot;
		VisualElement _provinceInfoRoot;
		int _lastOrgAgentSlotCount = -1;
		bool _orgPanelOpen;
		LensSwitcherView _lensSwitcher;
		OrgLensCountryView _orgLensCountryView;
		ActionLogView _actionLog;
		ActionConfig _actionConfig;
		ActionVisualConfig _actionVisualConfig;
		CardPlayAnimator _cardPlayAnimator;
		CountryConfig _countryConfig;
		GameSettings _gameSettings;
		List<WinConditionHintRowState> _winConditionRows = new();
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
		DebugCardAvailabilityView _selectedCountryCardDebug;
		DebugCardAvailabilityView _selectedOrgCardDebug;
		UIPointerState _pointerState;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, GameMenuDocument gameMenu, LeaderboardWindowDocument leaderboardWindow, GoalsWindowDocument goalsWindow, WarProgressWindowDocument warProgressWindow, OrgInfoDocument orgInfoDocument, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator, CountryConfig countryConfig, IFlyTextNotifier flyText, GameSettings gameSettings, UIPointerState pointerState) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_flyText = flyText;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_gameMenu = gameMenu;
			_leaderboardWindow = leaderboardWindow;
			_goalsWindow = goalsWindow;
			_warProgressWindow = warProgressWindow;
			_orgInfoDocument = orgInfoDocument;
			_actionConfig = actionConfig;
			_actionVisualConfig = actionVisualConfig;
			_cardPlayAnimator = cardPlayAnimator;
			_countryConfig = countryConfig;
			_gameSettings = gameSettings;
			_pointerState = pointerState;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			_root = _document.rootVisualElement;
			var root = _root;
			_pointerState.RuntimePanel = root.panel;

			_tooltip = new TooltipSystem(root.Q("hud-root"));
			_timeView = new TimeView(
				root.Q("time-panel"),
				OnPauseToggle,
				OnSpeedChange);
			_orgLensCountryView = new OrgLensCountryView(
				root.Q("org-lens-country-info"),
				_loc,
				_resourceConfig,
				_tooltip,
				_orgVisualConfig);

			var playerOrgRoot = root.Q("player-country");
			if (playerOrgRoot != null) {
				playerOrgRoot.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && playerOrgRoot.ContainsPoint(e.localPosition)) {
						ToggleOrgInfo();
					}
				});
			}
		}

		void Start() {
			_countryInfoRoot = _root.Q("country-info");
			_countryInfo = new CountryInfoView(_countryInfoRoot, _loc, _resourceConfig, _characterConfig, _tooltip, _characterVisualConfig, _actionConfig, _actionVisualConfig, _countryVisualConfig, _orgVisualConfig);
			_countryInfo.OnSubPanelOpened += HandleOrgSubPanelOpened;
			_countryInfo.OnCountryActionCardClicked += HandleCountryActionCardClicked;
			_countryInfo.OnRelatedCountryFlagClicked += HandleRelatedCountryFlagClicked;
			_provinceInfoRoot = _root.Q("province-info");
			_provinceInfo = new ProvinceInfoView(_provinceInfoRoot, _loc, _resourceConfig, _tooltip, _countryVisualConfig);
			_provinceInfo.OnCountryRowClicked += HandleProvinceInfoCountryRowClicked;
			_playerOrgView = new PlayerOrgView(_root.Q("player-country"), _loc, _resourceConfig, _tooltip, _orgVisualConfig);
			_lensSwitcher = new LensSwitcherView(_root.Q("lens-switcher"), _tooltip, _loc);
			_lensSwitcher.OnLensSelected = OnLensSelected;
			_warIconsView = new WarIconsView(
				_root.Q("war-icons"),
				_loc,
				_countryVisualConfig,
				_tooltip,
				warId => _warProgressWindow?.Open(warId));
			_warIconsView.Refresh(_state.WarIcons);
			_actionLog = new ActionLogView(_root, _root.Q("action-log"), _root.Q("top-right-panel"), _loc, _countryVisualConfig, _orgVisualConfig);
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened += HandleOrgSubPanelOpened;
			}
			_cardPlayAnimator?.SetCountryActionsView(_countryInfo.ActionsView);
			var root = _document.rootVisualElement;
			_btnMenu = root.Q<Button>("btn-menu");
			_btnLeaderboard = root.Q<Button>("btn-leaderboard");
			_btnGoals = root.Q<Button>("btn-goals");
			if (_btnMenu != null) {
				_btnMenu.clicked += () => _gameMenu?.Show();
			}
			if (_btnLeaderboard != null) {
				_btnLeaderboard.clicked += () => _leaderboardWindow?.Show();
				RefreshLeaderboardButtonText();
			}
			if (_btnGoals != null) {
				_btnGoals.clicked += () => _goalsWindow?.Show();
				RefreshGoalsButtonText();
			}

			_btnDebugToggle = root.Q<Button>("btn-debug-toggle");
			_debugPanel = root.Q("debug-panel");
			_btnSelectedCountryDebugMenu = root.Q<Button>("btn-selected-country-debug-menu");
			_selectedCountryDebugMenu = root.Q("selected-country-debug-menu");
			_btnSelectedOrgDebugMenu = root.Q<Button>("btn-selected-org-debug-menu");
			_selectedOrgDebugMenu = root.Q("selected-org-debug-menu");
			_btnEcsViewer = root.Q<Button>("btn-ecs-viewer");

			_btnDebugToggle.clicked += ToggleDebugPanel;
			_btnEcsViewer.clicked += OpenEcsViewer;
			RegisterDebugMenuToggle(_btnSelectedCountryDebugMenu, _selectedCountryDebugMenu, "Selected country");
			RegisterDebugMenuToggle(_btnSelectedOrgDebugMenu, _selectedOrgDebugMenu, "Selected org");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-characters"), root.Q("selected-country-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-relations"), root.Q("selected-country-relations"), "Relations");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-deck"), root.Q("selected-country-deck"), "Deck");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-hand"), root.Q("selected-country-hand"), "Hand");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-characters"), root.Q("selected-org-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-deck"), root.Q("selected-org-deck"), "Deck");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-hand"), root.Q("selected-org-hand"), "Hand");
#if UNITY_WEBGL && !UNITY_EDITOR
			_btnEcsViewer.style.display = DisplayStyle.None;
#endif

			_controlDebugRow = root.Q("control-debug-row");
			var btnControlPlus  = root.Q<Button>("btn-control-plus");
			var btnControlMinus = root.Q<Button>("btn-control-minus");
			if (btnControlPlus != null) {
				btnControlPlus.clicked += () => PushControlCommand(+5);
			}
			if (btnControlMinus != null) {
				btnControlMinus.clicked += () => PushControlCommand(-5);
			}
			RefreshControlDebugRow();

			var btnGoldPlus  = root.Q<Button>("btn-gold-plus");
			var btnGoldMinus = root.Q<Button>("btn-gold-minus");
			if (btnGoldPlus != null) {
				btnGoldPlus.clicked += () => PushChangeGoldCommand(+1000);
			}
			if (btnGoldMinus != null) {
				btnGoldMinus.clicked += () => PushChangeGoldCommand(-1000);
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

			_selectedCountryCardDebug = new DebugCardAvailabilityView(
				root.Q("selected-country-deck"),
				root.Q("selected-country-hand"),
				_loc,
				_actionConfig,
				PushDebugDrawCountryCardCommand,
				PushDebugDiscardCountryCardCommand);
			_selectedOrgCardDebug = new DebugCardAvailabilityView(
				root.Q("selected-org-deck"),
				root.Q("selected-org-hand"),
				_loc,
				_actionConfig,
				PushDebugDrawOrgCardCommand,
				PushDebugDiscardOrgCardCommand);

			int availableCountryCount = _countryConfig != null ? CountAvailableCountries(_countryConfig) : 0;
			var (_, _, winConditionRows) = WinConditionHintProjector.Build(_gameSettings?.CompletionCondition, availableCountryCount);
			_winConditionRows = winConditionRows;

			RebuildOrgCharDebugButtons();
			RefreshSelectedCountryCharacterDebugButtons();
			RefreshSelectedProvinceDebugMenu();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
			RefreshDebugCardAvailability();
		}

		void BuildProvinceDebugUi() {
			_provinceDebugContainer = _root.Q("province-debug-container");
			if (_provinceDebugContainer == null) { return; }

			_provinceCountryDropdown = new DropdownField();
			_provinceCountryDropdown.AddToClassList("debug-panel-button");
			_provinceCountryDropdown.RegisterValueChangedCallback(_ => RefreshProvinceActionButtons());
			_provinceDebugContainer.Add(_provinceCountryDropdown);

			_btnChangeProvinceOwner = new Button(PushChangeProvinceOwnerCommand) { text = "Change owner" };
			_btnChangeProvinceOwner.AddToClassList("gs-btn");
			_btnChangeProvinceOwner.AddToClassList("gs-btn--small");
			_btnChangeProvinceOwner.AddToClassList("debug-panel-button");
			_provinceDebugContainer.Add(_btnChangeProvinceOwner);

			_btnChangeProvinceOccupation = new Button(PushSetProvinceOccupationCommand) { text = "Change occupation" };
			_btnChangeProvinceOccupation.AddToClassList("gs-btn");
			_btnChangeProvinceOccupation.AddToClassList("gs-btn--small");
			_btnChangeProvinceOccupation.AddToClassList("debug-panel-button");
			_provinceDebugContainer.Add(_btnChangeProvinceOccupation);

			_btnResetProvinceOccupation = new Button(PushClearProvinceOccupationCommand) { text = "Reset occupation" };
			_btnResetProvinceOccupation.AddToClassList("gs-btn");
			_btnResetProvinceOccupation.AddToClassList("gs-btn--small");
			_btnResetProvinceOccupation.AddToClassList("debug-panel-button");
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

		void ToggleDebugPanel() {
			_debugPanelOpen = !_debugPanelOpen;
			_debugPanel.style.display = _debugPanelOpen ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void RegisterDebugMenuToggle(Button button, VisualElement menu, string label) {
			if (button == null || menu == null) {
				return;
			}

			menu.style.display = DisplayStyle.None;
			button.text = $"> {label}";
			button.RegisterCallback<PointerUpEvent>(e => {
				if (!button.enabledSelf || e.button != 0 || !button.ContainsPoint(e.localPosition)) {
					return;
				}

				bool isOpen = menu.style.display != DisplayStyle.None;
				menu.style.display = isOpen ? DisplayStyle.None : DisplayStyle.Flex;
				button.text = $"{(isOpen ? ">" : "v")} {label}";
			});
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
			if (_cardPlayAnimator != null) { _cardPlayAnimator.OnCardPlayComplete += HandleCardPlayComplete; }
			_state.SelectedCountry.PropertyChanged    += HandleCountryChanged;
			_state.PlayerOrganization.PropertyChanged += HandlePlayerOrgChanged;
			_state.Time.PropertyChanged               += HandleTimeChanged;
			_state.Locale.PropertyChanged             += HandleLocaleChanged;
			_state.PlayerOrganization.Resources.PropertyChanged    += HandlePlayerResourcesChanged;
			_state.SelectedCountry.Resources.PropertyChanged  += HandleSelectedResourcesChanged;
			_state.OrgLensOrganizationResources.PropertyChanged += HandleOrgLensResourcesChanged;
			_state.SelectedCountry.Control.PropertyChanged  += HandleControlChanged;
			_state.SelectedCountry.Characters.PropertyChanged += HandleCharactersChanged;
			_state.SelectedCountry.CountryActions.PropertyChanged += HandleCountryActionsChanged;
			_state.SelectedCountry.Relations.PropertyChanged += HandleRelationsChanged;
			_state.SelectedCountry.Wars.PropertyChanged += HandleWarsChanged;
			_state.MapLens.PropertyChanged            += HandleLensChanged;
			_state.OrgMap.PropertyChanged             += HandleOrgMapChanged;
			_state.PlayerOrganization.Characters.PropertyChanged += HandleOrgCharactersChanged;
			_state.PlayerOrganization.Actions.PropertyChanged += HandleOrgActionsChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged += HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged += HandleSelectedProvinceChanged;
			_state.SelectedProvince.Resources.PropertyChanged += HandleSelectedProvinceResourcesChanged;
			_state.ProvinceOwnership.PropertyChanged += HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged += HandleProvinceOccupationChanged;
			_state.GameLog.PropertyChanged += HandleGameLogChanged;
			_state.WarIcons.PropertyChanged += HandleWarIconsChanged;
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
			_warIconsView?.Refresh(_state.WarIcons);
			RefreshCountryViews();
			RefreshProvinceInfoView();
			RefreshControlDebugRow();
			RefreshSelectedCountryCharacterDebugButtons();
			RefreshSelectedProvinceDebugMenu();
			_timeView.Refresh(_state.Time);
			_actionLog?.Refresh(_state.GameLog);
			_lastNotifiedLogSequenceId = HighestSequenceId(_state.GameLog);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			if (_cardPlayAnimator != null) { _cardPlayAnimator.OnCardPlayComplete -= HandleCardPlayComplete; }
			_state.SelectedCountry.PropertyChanged    -= HandleCountryChanged;
			_state.PlayerOrganization.PropertyChanged -= HandlePlayerOrgChanged;
			_state.Time.PropertyChanged               -= HandleTimeChanged;
			_state.Locale.PropertyChanged             -= HandleLocaleChanged;
			_state.PlayerOrganization.Resources.PropertyChanged    -= HandlePlayerResourcesChanged;
			_state.SelectedCountry.Resources.PropertyChanged  -= HandleSelectedResourcesChanged;
			_state.OrgLensOrganizationResources.PropertyChanged -= HandleOrgLensResourcesChanged;
			_state.SelectedCountry.Control.PropertyChanged  -= HandleControlChanged;
			_state.SelectedCountry.Characters.PropertyChanged -= HandleCharactersChanged;
			_state.SelectedCountry.CountryActions.PropertyChanged -= HandleCountryActionsChanged;
			_state.SelectedCountry.Relations.PropertyChanged -= HandleRelationsChanged;
			_state.SelectedCountry.Wars.PropertyChanged -= HandleWarsChanged;
			_state.MapLens.PropertyChanged            -= HandleLensChanged;
			_state.OrgMap.PropertyChanged             -= HandleOrgMapChanged;
			_state.PlayerOrganization.Characters.PropertyChanged -= HandleOrgCharactersChanged;
			_state.PlayerOrganization.Actions.PropertyChanged -= HandleOrgActionsChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged -= HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged -= HandleSelectedProvinceChanged;
			_state.SelectedProvince.Resources.PropertyChanged -= HandleSelectedProvinceResourcesChanged;
			_state.ProvinceOwnership.PropertyChanged -= HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged -= HandleProvinceOccupationChanged;
			_state.GameLog.PropertyChanged -= HandleGameLogChanged;
			_state.WarIcons.PropertyChanged -= HandleWarIconsChanged;
			_lastOrgAgentSlotCount = -1;
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened -= HandleOrgSubPanelOpened;
			}
			if (_countryInfo != null) { _countryInfo.OnSubPanelOpened -= HandleOrgSubPanelOpened; }
			if (_countryInfo != null) { _countryInfo.OnCountryActionCardClicked -= HandleCountryActionCardClicked; }
			if (_countryInfo != null) { _countryInfo.OnRelatedCountryFlagClicked -= HandleRelatedCountryFlagClicked; }
			if (_provinceInfo != null) { _provinceInfo.OnCountryRowClicked -= HandleProvinceInfoCountryRowClicked; }
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
			bool isOrgLens = _state.MapLens.Lens == MapLens.Org;
			bool isProvinceLens = _state.MapLens.Lens == MapLens.Province;
			if (isProvinceLens) {
				if (_countryInfoRoot != null) {
					_countryInfoRoot.style.display = DisplayStyle.None;
				}
				_orgLensCountryView?.Hide();
				_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
				RefreshDebugCardAvailability();
				return;
			}
			if (isOrgLens) {
				if (_countryInfoRoot != null) {
					_countryInfoRoot.style.display = DisplayStyle.None;
				}
				_orgLensCountryView?.Refresh(
					_state.SelectedCountry,
					_state.OrgMap,
					_state.SelectedCountry.Control,
					_state.OrgLensOrganizationResources);
			} else {
				_orgLensCountryView?.Hide();
				_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
				if (_orgPanelOpen && _countryInfoRoot != null) {
					_countryInfoRoot.style.display = DisplayStyle.None;
				}
			}
			_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
			RefreshDebugCardAvailability();
		}

		void RefreshDebugCardAvailability() {
			if (_state == null) {
				return;
			}
			double gold = GetPlayerGold();
			if (_selectedCountryCardDebug != null) {
				var countryActions = _state.SelectedCountry.CountryActions;
				_selectedCountryCardDebug.RefreshDeck(countryActions.Deck);
				_selectedCountryCardDebug.RefreshHand(countryActions.Hand, gold);
			}
			if (_selectedOrgCardDebug != null) {
				var orgActions = _state.PlayerOrganization.Actions;
				_selectedOrgCardDebug.RefreshDeck(orgActions.Deck);
				_selectedOrgCardDebug.RefreshHand(orgActions.Hand, gold);
			}
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

		void PushDebugDrawOrgCardCommand(string actionId, string targetCountryId) {
			if (_state == null || !_state.PlayerOrganization.IsValid) {
				return;
			}
			if (string.IsNullOrEmpty(actionId)) {
				return;
			}
			_commands.Push(new DebugDrawCardCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				CountryId = "",
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

		void PushDebugDiscardOrgCardCommand(string actionId, string targetCountryId, int slotIndex) {
			if (_state == null || !_state.PlayerOrganization.IsValid) {
				return;
			}
			if (string.IsNullOrEmpty(actionId)) {
				return;
			}
			_commands.Push(new DebugDiscardCardCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				CountryId = "",
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

		void RefreshProvinceInfoView() {
			if (_provinceInfo == null || _state == null) {
				return;
			}
			bool visible = _state.MapLens.Lens == MapLens.Province && _state.SelectedProvince.IsValid;
			string provinceId = _state.SelectedProvince.ProvinceId;
			string ownerId = GetProvinceOwner(provinceId);
			string occupierId = GetProvinceOccupier(provinceId);
			_provinceInfo.Refresh(visible, provinceId, ownerId, occupierId, _state.SelectedProvince.Resources);
		}

		void HandleProvinceInfoCountryRowClicked(string countryId) {
			if (string.IsNullOrEmpty(countryId)) {
				return;
			}
			_commands.Push(new SelectCountryCommand(countryId));
			_commands.Push(new ChangeLensCommand { Lens = MapLens.Political });
		}

		void HandleRelatedCountryFlagClicked(string countryId) {
			if (string.IsNullOrEmpty(countryId)) {
				return;
			}
			_commands.Push(new SelectCountryCommand(countryId));
		}

		void HandleSelectedProvinceResourcesChanged(object sender, PropertyChangedEventArgs e) {
			RefreshProvinceInfoView();
		}

		void RefreshLeaderboardButtonText() {
			if (_btnLeaderboard == null || _loc == null) {
				return;
			}
			string text = _loc.Get("hud.leaderboard");
			_btnLeaderboard.text = string.IsNullOrEmpty(text) || text == "hud.leaderboard" ? "Leaderboard" : text;
		}

		void RefreshGoalsButtonText() {
			if (_btnGoals == null || _loc == null) {
				return;
			}
			string text = _loc.Get("hud.goals");
			_btnGoals.text = string.IsNullOrEmpty(text) || text == "hud.goals" ? "Goals" : text;
		}

		void RefreshControlDebugRow() {
			if (_controlDebugRow == null) {
				return;
			}
			_controlDebugRow.style.display =
				_state != null && _state.SelectedCountry.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void PushChangeGoldCommand(double amount) {
			if (_state == null || !_state.PlayerOrganization.IsValid) { return; }
			if (amount > 0) {
				AnimateGoldDebug(amount).Forget();
				return;
			}
			_commands.Push(new GS.Game.Commands.DebugChangeGoldCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				Amount = amount
			});
		}

		async UniTaskVoid AnimateGoldDebug(double amount) {
			AnimatableDouble goldAnimatable = null;
			foreach (var res in _state.PlayerOrganization.Resources.Resources) {
				if (res.ResourceId == "gold") { goldAnimatable = res.Value; break; }
			}
			if (goldAnimatable == null) { return; }
			var barrier = goldAnimatable.Hold(-amount);
			_commands.Push(new GS.Game.Commands.DebugChangeGoldCommand {
				OrgId = _state.PlayerOrganization.OrgId,
				Amount = amount
			});
			await UniTask.NextFrame();
			barrier.Release(3.0f);
			await UniTask.WaitUntil(() => barrier.IsComplete);
		}

		void PushControlCommand(int delta) {
			if (_state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) {
				return;
			}
			_commands.Push(new ChangeControlCommand {
				OrgId     = _state.PlayerOrganization.OrgId,
				CountryId = _state.SelectedCountry.CountryId,
				Delta     = delta
			});
		}

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			RefreshControlDebugRow();
			RefreshSelectedCountryCharacterDebugButtons();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
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

		void HandlePlayerOrgChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandleControlChanged(object sender, PropertyChangedEventArgs e) {
			if (_cardPlayAnimator != null && _cardPlayAnimator.IsPlaying) { return; }
			RefreshCountryViews();
		}

		void HandleCardPlayComplete() => RefreshCountryViews();

		void HandleTimeChanged(object sender, PropertyChangedEventArgs e) {
			_timeView.Refresh(_state.Time);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			_loc.SetLocale(_state.Locale.Locale);
			_tooltip?.HideAll();
			_warIconsView?.Refresh(_state.WarIcons);
			RefreshLeaderboardButtonText();
			RefreshGoalsButtonText();
			RefreshCountryViews();
			RefreshProvinceInfoView();
			_timeView.Refresh(_state.Time);
		}

		void HandlePlayerResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
			RefreshDebugCardAvailability();
		}

		void HandleSelectedResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
		}

		void HandleOrgLensResourcesChanged(object sender, PropertyChangedEventArgs e) {
			if (_state.MapLens.Lens != MapLens.Org) {
				return;
			}
			_orgLensCountryView?.Refresh(
				_state.SelectedCountry,
				_state.OrgMap,
				_state.SelectedCountry.Control,
				_state.OrgLensOrganizationResources);
		}

		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandleCountryActionsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

		void HandleOrgActionsChanged(object sender, PropertyChangedEventArgs e) => RefreshDebugCardAvailability();

		void HandleRelationsChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
		}

		void HandleWarsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

		void HandleCountryActionCardClicked(string actionId, string targetCharId, VisualElement el) {
			if (_cardPlayAnimator == null || _state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) { return; }
			_cardPlayAnimator.StartCountryCardPlay(
				_state.PlayerOrganization.OrgId,
				_state.SelectedCountry.CountryId,
				actionId, el, targetCharId);
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
			RefreshCountryViews();
			RefreshProvinceInfoView();
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleSelectedProvinceChanged(object sender, PropertyChangedEventArgs e) {
			RefreshProvinceInfoView();
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleProvinceOwnershipChanged(object sender, PropertyChangedEventArgs e) {
			_lastProvinceIdForDropdown = "";
			RefreshProvinceInfoView();
			RefreshSelectedProvinceDebugMenu();
		}

		void HandleProvinceOccupationChanged(object sender, PropertyChangedEventArgs e) {
			RefreshProvinceInfoView();
			RefreshProvinceActionButtons();
		}

		void HandleGameLogChanged(object sender, PropertyChangedEventArgs e) {
			_actionLog?.Refresh(_state.GameLog);
			NotifyNewLogEntries();
		}

		void HandleWarIconsChanged(object sender, PropertyChangedEventArgs e) {
			_warIconsView?.Refresh(_state.WarIcons);
		}

		void NotifyNewLogEntries() {
			if (_flyText == null) { return; }
			string playerOrgId = _state.PlayerOrganization.OrgId;
			long maxSeen = _lastNotifiedLogSequenceId;
			foreach (var entry in _state.GameLog.Entries) {
				if (entry.SequenceId <= _lastNotifiedLogSequenceId) { continue; }
				if (entry.OrgId == playerOrgId) {
					if (entry.Kind == GameLogEntryKind.Control) {
						_flyText.NotifyRaw(GameLogLineFormatter.BuildControlLine(entry, _loc, _countryVisualConfig, _orgVisualConfig));
					} else if (entry.Kind == GameLogEntryKind.Opinion) {
						_flyText.NotifyRaw(GameLogLineFormatter.BuildOpinionLine(entry, _loc, _countryVisualConfig, _orgVisualConfig));
					} else if (entry.Kind == GameLogEntryKind.Relation) {
						_flyText.NotifyRaw(GameLogLineFormatter.BuildRelationLine(entry, _loc, _countryVisualConfig, _orgVisualConfig));
					}
				}
				if (entry.SequenceId > maxSeen) { maxSeen = entry.SequenceId; }
			}
			_lastNotifiedLogSequenceId = maxSeen;
		}

		static long HighestSequenceId(GameLogState state) {
			long max = -1;
			foreach (var entry in state.Entries) {
				if (entry.SequenceId > max) { max = entry.SequenceId; }
			}
			return max;
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
			if (_state?.Leaderboard?.Organizations == null) { return ""; }
			string playerOrgId = GetPlayerOrgId();
			string opponentOrgId = "";
			foreach (var entry in _state.Leaderboard.Organizations) {
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

		void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

		void OnLensSelected(MapLens lens) {
			_commands.Push(new ChangeLensCommand { Lens = lens });
		}

		void ToggleOrgInfo() {
			if (!(_gameSettings?.FeatureFlags?.ShowPlayerOrgControls ?? true)) { return; }
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
				winBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && winBtn.ContainsPoint(e.localPosition)) {
						PushForceCompletionCondition(GetPlayerOrgId(), capturedRow);
					}
				});
				orgMiscDebugContainer.Add(winBtn);

				var loseBtn = new Button();
				loseBtn.text = $"Lose ({label})";
				loseBtn.AddToClassList("gs-btn");
				loseBtn.AddToClassList("gs-btn--small");
				loseBtn.AddToClassList("debug-panel-button");
				loseBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && loseBtn.ContainsPoint(e.localPosition)) {
						PushForceCompletionCondition(GetOpponentOrgId(), capturedRow);
					}
				});
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

		void HandleOrgSubPanelOpened(bool anyOpen) {
			var lensSwitcherEl = _root.Q("lens-switcher");
			if (lensSwitcherEl != null) {
				lensSwitcherEl.style.display = anyOpen ? DisplayStyle.None : DisplayStyle.Flex;
			}
			if (!anyOpen) {
				_tooltip?.HideAll();
			}
		}

		void HandleControlTickChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			_countryInfo?.RefreshUsedControl();
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
	}
}
