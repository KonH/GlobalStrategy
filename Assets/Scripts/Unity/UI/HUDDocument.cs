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
		PlayerTasksView _playerTasksView;
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
		CardDrawView _cardDrawView;
		CardDrawAnimator _cardDrawAnimator;
		ModalState _modalState;
		bool _viewEventsSubscribed;
		bool _started;
		int _enableGeneration;
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
		DebugCardAvailabilityView _myOrgCardDebug;
		DebugCardAvailabilityView _selectedOrgCardDebug;
		UIPointerState _pointerState;
		CountryActionsVisibility _countryActionsVisibility;
		DebugOrgCardVisibility _debugOrgCardVisibility;
		TutorialPresentationTriggers _presentationTriggers;
		TutorialHighlightView _tutorialHighlightView;
		VisualElement _cardDrawOverlay;
		Label _selectedCountryDebugName;
		Label _selectedOrgDebugName;
		VisualElement _controlOrgDebugList;
		VisualElement _controlOrgDebugContainer;
		DropdownField _controlOrgDropdown;
		Button _btnControlOrgPlus;
		Button _btnControlOrgMinus;
		readonly List<string> _controlOrgDropdownOrgIds = new();

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, GameMenuDocument gameMenu, LeaderboardWindowDocument leaderboardWindow, GoalsWindowDocument goalsWindow, WarProgressWindowDocument warProgressWindow, OrgInfoDocument orgInfoDocument, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator, CountryConfig countryConfig, IFlyTextNotifier flyText, GameSettings gameSettings, UIPointerState pointerState, ModalState modalState, CountryActionsVisibility countryActionsVisibility, DebugOrgCardVisibility debugOrgCardVisibility, TutorialPresentationTriggers presentationTriggers) {
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
			_modalState = modalState;
			_countryActionsVisibility = countryActionsVisibility;
			_debugOrgCardVisibility = debugOrgCardVisibility;
			_presentationTriggers = presentationTriggers;
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
			_countryInfo = new CountryInfoView(_countryInfoRoot, _loc, _resourceConfig, _characterConfig, _tooltip, _characterVisualConfig, _actionConfig, _actionVisualConfig, _countryVisualConfig, _orgVisualConfig, _gameSettings, _countryActionsVisibility);
			_provinceInfoRoot = _root.Q("province-info");
			_provinceInfo = new ProvinceInfoView(_provinceInfoRoot, _loc, _resourceConfig, _tooltip, _countryVisualConfig);
			_playerOrgView = new PlayerOrgView(_root.Q("player-country"), _loc, _resourceConfig, _tooltip, _orgVisualConfig);
			var playerTasksRoot = _root.Q("player-tasks");
			if (playerTasksRoot != null) {
				_playerTasksView = new PlayerTasksView(playerTasksRoot, _loc, _resourceConfig);
				_playerTasksView.Refresh(_state.ActiveTasks);
			}
			var tutorialHighlightRoot = _root.Q("tutorial-highlight");
			if (tutorialHighlightRoot != null) {
				_tutorialHighlightView = new TutorialHighlightView(tutorialHighlightRoot, ResolveHighlightTarget);
				_tutorialHighlightView.Refresh(_state.ActiveTasks);
			}
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
			_cardPlayAnimator?.SetCountryActionsView(_countryInfo.ActionsView);
			_cardDrawOverlay = _root.Q("card-draw-overlay");
			var cardDrawRow = _root.Q("card-draw-row");
			if (_cardDrawOverlay == null || cardDrawRow == null || _countryInfo.ActionsView == null) {
				Debug.LogError("[HUDDocument] Card draw UI or country actions view is missing.", this);
			} else {
				_cardDrawView = new CardDrawView(_cardDrawOverlay, cardDrawRow, _actionVisualConfig);
				_cardDrawAnimator = new CardDrawAnimator(
					_state,
					_commands,
					_modalState,
					_document,
					_countryInfo,
					_countryInfo.ActionsView,
					_cardDrawView);
			}
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
			_btnMyOrgDebugMenu = root.Q<Button>("btn-my-org-debug-menu");
			_myOrgDebugMenu = root.Q("my-org-debug-menu");
			_btnSelectedOrgDebugMenu = root.Q<Button>("btn-selected-org-debug-menu");
			_selectedOrgDebugMenu = root.Q("selected-org-debug-menu");
			_selectedCountryDebugName = root.Q<Label>("selected-country-debug-name");
			_selectedOrgDebugName = root.Q<Label>("selected-org-debug-name");
			_btnEcsViewer = root.Q<Button>("btn-ecs-viewer");

			_btnDebugToggle.clicked += ToggleDebugPanel;
			_btnEcsViewer.clicked += OpenEcsViewer;

			_btnFpsToggle = root.Q<Button>("btn-fps-toggle");
			_fpsLabel = root.Q<Label>("fps-label");
			if (_btnFpsToggle != null) {
				_btnFpsToggle.clicked += ToggleFpsDisplay;
			}
			SetFpsEnabled(false);
			RegisterDebugMenuToggle(_btnSelectedCountryDebugMenu, _selectedCountryDebugMenu, "Selected country");
			RegisterDebugMenuToggle(_btnMyOrgDebugMenu, _myOrgDebugMenu, "My org");
			RegisterDebugMenuToggle(_btnSelectedOrgDebugMenu, _selectedOrgDebugMenu, "Selected org");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-characters"), root.Q("selected-country-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-country-relations"), root.Q("selected-country-relations"), "Relations");
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-characters"), root.Q("my-org-characters"), "Characters");
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-deck"), root.Q("my-org-deck"), "Deck",
				open => _debugOrgCardVisibility.MyOrgDeckOpen = open);
			RegisterDebugMenuToggle(root.Q<Button>("btn-my-org-hand"), root.Q("my-org-hand"), "Hand",
				open => _debugOrgCardVisibility.MyOrgHandOpen = open);
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-deck"), root.Q("selected-org-deck"), "Deck",
				open => _debugOrgCardVisibility.SelectedOrgDeckOpen = open);
			RegisterDebugMenuToggle(root.Q<Button>("btn-selected-org-hand"), root.Q("selected-org-hand"), "Hand",
				open => _debugOrgCardVisibility.SelectedOrgHandOpen = open);
#if UNITY_WEBGL && !UNITY_EDITOR
			_btnEcsViewer.style.display = DisplayStyle.None;
#endif

			var btnGoldPlus  = root.Q<Button>("btn-gold-plus");
			var btnGoldMinus = root.Q<Button>("btn-gold-minus");
			if (btnGoldPlus != null) {
				btnGoldPlus.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && btnGoldPlus.ContainsPoint(e.localPosition)) {
						PushChangeGoldCommand(+1000);
					}
				});
			}
			if (btnGoldMinus != null) {
				btnGoldMinus.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && btnGoldMinus.ContainsPoint(e.localPosition)) {
						PushChangeGoldCommand(-1000);
					}
				});
			}

			var btnSelectedOrgGoldPlus  = root.Q<Button>("btn-selected-org-gold-plus");
			var btnSelectedOrgGoldMinus = root.Q<Button>("btn-selected-org-gold-minus");
			if (btnSelectedOrgGoldPlus != null) {
				btnSelectedOrgGoldPlus.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && btnSelectedOrgGoldPlus.ContainsPoint(e.localPosition)) {
						PushSelectedOrgChangeGoldCommand(+1000);
					}
				});
			}
			if (btnSelectedOrgGoldMinus != null) {
				btnSelectedOrgGoldMinus.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && btnSelectedOrgGoldMinus.ContainsPoint(e.localPosition)) {
						PushSelectedOrgChangeGoldCommand(-1000);
					}
				});
			}

			var btnSelectedOrgDestroy = root.Q<Button>("btn-selected-org-destroy");
			if (btnSelectedOrgDestroy != null) {
				btnSelectedOrgDestroy.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && btnSelectedOrgDestroy.ContainsPoint(e.localPosition)) {
						PushForceOrgDestroyCommand(GetSelectedOrgId());
					}
				});
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
			RebuildControlOrgDropdown();
			RefreshControlOrgDebugList();
			_started = true;
			SubscribeViewEvents();
			_countryInfo.ActionsView?.SetPresentationBusy(_cardPlayAnimator?.IsPlaying ?? false);
			RefreshCountryViews();
			_cardDrawAnimator?.SetRestorationEnabled(true);
			_cardDrawAnimator?.RestorePendingOfferIfIdle();
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
			_btnChangeProvinceOwner.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnChangeProvinceOwner.enabledSelf && _btnChangeProvinceOwner.ContainsPoint(e.localPosition)) {
					PushChangeProvinceOwnerCommand();
				}
			});
			_provinceDebugContainer.Add(_btnChangeProvinceOwner);

			_btnChangeProvinceOccupation = new Button { text = "Change occupation" };
			_btnChangeProvinceOccupation.AddToClassList("gs-btn");
			_btnChangeProvinceOccupation.AddToClassList("gs-btn--small");
			_btnChangeProvinceOccupation.AddToClassList("debug-panel-button");
			_btnChangeProvinceOccupation.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnChangeProvinceOccupation.enabledSelf && _btnChangeProvinceOccupation.ContainsPoint(e.localPosition)) {
					PushSetProvinceOccupationCommand();
				}
			});
			_provinceDebugContainer.Add(_btnChangeProvinceOccupation);

			_btnResetProvinceOccupation = new Button { text = "Reset occupation" };
			_btnResetProvinceOccupation.AddToClassList("gs-btn");
			_btnResetProvinceOccupation.AddToClassList("gs-btn--small");
			_btnResetProvinceOccupation.AddToClassList("debug-panel-button");
			_btnResetProvinceOccupation.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnResetProvinceOccupation.enabledSelf && _btnResetProvinceOccupation.ContainsPoint(e.localPosition)) {
					PushClearProvinceOccupationCommand();
				}
			});
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
			_btnControlOrgPlus.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnControlOrgPlus.ContainsPoint(e.localPosition)) {
					PushControlOrgCommand(+10);
				}
			});
			row.Add(_btnControlOrgPlus);

			_btnControlOrgMinus = new Button { text = "Control-10" };
			_btnControlOrgMinus.AddToClassList("gs-btn");
			_btnControlOrgMinus.AddToClassList("gs-btn--small");
			_btnControlOrgMinus.AddToClassList("debug-panel-button");
			_btnControlOrgMinus.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnControlOrgMinus.ContainsPoint(e.localPosition)) {
					PushControlOrgCommand(-10);
				}
			});
			row.Add(_btnControlOrgMinus);
		}

		void ToggleDebugPanel() {
			_debugPanelOpen = !_debugPanelOpen;
			_debugPanel.style.display = _debugPanelOpen ? DisplayStyle.Flex : DisplayStyle.None;
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
			button.RegisterCallback<PointerUpEvent>(e => {
				if (!button.enabledSelf || e.button != 0 || !button.ContainsPoint(e.localPosition)) {
					return;
				}

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
				Debug.LogWarning("[HUDDocument] ECS Viewer URL is not available — is the bridge running?");
				return;
			}
			Application.OpenURL(url);
		}

		void OnEnable() {
			int enableGeneration = ++_enableGeneration;
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
			_state.MyOrgCardAvailability.PropertyChanged += HandleMyOrgCardAvailabilityChanged;
			_state.SelectedOrgCardAvailability.PropertyChanged += HandleSelectedOrgCardAvailabilityChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged += HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged += HandleSelectedProvinceChanged;
			_state.SelectedProvince.Resources.PropertyChanged += HandleSelectedProvinceResourcesChanged;
			_state.ProvinceOwnership.PropertyChanged += HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged += HandleProvinceOccupationChanged;
			_state.GameLog.PropertyChanged += HandleGameLogChanged;
			_state.WarIcons.PropertyChanged += HandleWarIconsChanged;
			_state.ActiveTasks.PropertyChanged += HandleActiveTasksChanged;
			_state.LastFrameEffects.PropertyChanged += HandleLastFrameEffectsChanged;
			_state.Leaderboard.PropertyChanged += HandleLeaderboardChanged;
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
			_warIconsView?.Refresh(_state.WarIcons);
			_playerTasksView?.Refresh(_state.ActiveTasks);
			_tutorialHighlightView?.Refresh(_state.ActiveTasks);
			RefreshCountryViews();
			RefreshMyOrgCardAvailability();
			RefreshSelectedOrgCardAvailability();
			RefreshProvinceInfoView();
			RefreshSelectedCountryCharacterDebugButtons();
			RefreshSelectedOrgDebugMenuAvailability();
			RebuildControlOrgDropdown();
			RefreshSelectedProvinceDebugMenu();
			_timeView.Refresh(_state.Time);
			_actionLog?.Refresh(_state.GameLog);
			_lastNotifiedLogSequenceId = HighestSequenceId(_state.GameLog);
			if (_started) {
				_cardDrawAnimator?.SetRestorationEnabled(true);
				_cardDrawAnimator?.BeginResumeBarrier();
				ResumeAfterEnableAsync(enableGeneration).Forget();
			}
		}

		void OnDisable() {
			_enableGeneration++;
			_cardDrawAnimator?.SetRestorationEnabled(false);
			_cardDrawAnimator?.EndResumeBarrier();
			_cardDrawAnimator?.CancelAndWaitAsync().Forget();
			UnsubscribeViewEvents();
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
			_state.MyOrgCardAvailability.PropertyChanged -= HandleMyOrgCardAvailabilityChanged;
			_state.SelectedOrgCardAvailability.PropertyChanged -= HandleSelectedOrgCardAvailabilityChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged -= HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged -= HandleSelectedProvinceChanged;
			_state.SelectedProvince.Resources.PropertyChanged -= HandleSelectedProvinceResourcesChanged;
			_state.ProvinceOwnership.PropertyChanged -= HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged -= HandleProvinceOccupationChanged;
			_state.GameLog.PropertyChanged -= HandleGameLogChanged;
			_state.WarIcons.PropertyChanged -= HandleWarIconsChanged;
			_state.ActiveTasks.PropertyChanged -= HandleActiveTasksChanged;
			_state.LastFrameEffects.PropertyChanged -= HandleLastFrameEffectsChanged;
			_state.Leaderboard.PropertyChanged -= HandleLeaderboardChanged;
			_lastOrgAgentSlotCount = -1;
		}

		async UniTaskVoid ResumeAfterEnableAsync(int enableGeneration) {
			if (_cardDrawAnimator != null) {
				await _cardDrawAnimator.CancelAndWaitAsync();
			}
			if (enableGeneration != _enableGeneration || !isActiveAndEnabled) {
				return;
			}
			SubscribeViewEvents();
			RefreshCountryViews();
			_cardDrawAnimator?.SetRestorationEnabled(true);
			_cardDrawAnimator?.EndResumeBarrier();
			_countryInfo?.ActionsView?.SetPresentationBusy(_cardPlayAnimator?.IsPlaying ?? false);
			_cardDrawAnimator?.RestorePendingOfferIfIdle();
		}

		void SubscribeViewEvents() {
			if (_viewEventsSubscribed || _countryInfo == null) {
				return;
			}
			_countryInfo.OnSubPanelOpened += HandleOrgSubPanelOpened;
			_countryInfo.OnCountryActionCardClicked += HandleCountryActionCardClicked;
			_countryInfo.OnCountryActionCardDiscarded += HandleCountryActionCardDiscarded;
			_countryInfo.OnUnplayableCountryActionCardReleased += HandleUnplayableCountryActionCardReleased;
			_countryInfo.OnCountryActionCardDiscardUnaffordable += HandleCountryActionCardDiscardUnaffordable;
			_countryInfo.OnRelatedCountryFlagClicked += HandleRelatedCountryFlagClicked;
			if (_countryInfo.ActionsView != null) {
				_countryInfo.ActionsView.OnDrawRequested += HandleCountryActionDrawRequested;
			}
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened += HandleOrgSubPanelOpened;
			}
			if (_provinceInfo != null) {
				_provinceInfo.OnCountryRowClicked += HandleProvinceInfoCountryRowClicked;
			}
			_viewEventsSubscribed = true;
		}

		void UnsubscribeViewEvents() {
			if (!_viewEventsSubscribed) {
				return;
			}
			if (_countryInfo != null) {
				_countryInfo.OnSubPanelOpened -= HandleOrgSubPanelOpened;
				_countryInfo.OnCountryActionCardClicked -= HandleCountryActionCardClicked;
				_countryInfo.OnCountryActionCardDiscarded -= HandleCountryActionCardDiscarded;
				_countryInfo.OnUnplayableCountryActionCardReleased -= HandleUnplayableCountryActionCardReleased;
				_countryInfo.OnCountryActionCardDiscardUnaffordable -= HandleCountryActionCardDiscardUnaffordable;
				_countryInfo.OnRelatedCountryFlagClicked -= HandleRelatedCountryFlagClicked;
				if (_countryInfo.ActionsView != null) {
					_countryInfo.ActionsView.OnDrawRequested -= HandleCountryActionDrawRequested;
				}
			}
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened -= HandleOrgSubPanelOpened;
			}
			if (_provinceInfo != null) {
				_provinceInfo.OnCountryRowClicked -= HandleProvinceInfoCountryRowClicked;
			}
			_viewEventsSubscribed = false;
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
			PublishPresentationTriggers();
			if (_fpsEnabled) {
				UpdateFpsCounter();
			}
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

		void PublishPresentationTriggers() {
			if (_presentationTriggers == null || _state == null) {
				return;
			}

			bool selectedCountryOpen = _state.SelectedCountry.IsValid;
			_presentationTriggers.Set("uiOpened:selectedCountryPanel", selectedCountryOpen ? 1 : 0);

			bool charactersOpen = _countryInfo != null && _countryInfo.IsCharactersOpen;
			bool actionsOpen = _countryInfo != null && _countryInfo.IsActionsOpen;
			_presentationTriggers.Set("uiOpened:characterList", charactersOpen ? 1 : 0);
			_presentationTriggers.Set("uiOpened:actionsPanel", actionsOpen ? 1 : 0);

			bool goalsOpen = _goalsWindow != null && _goalsWindow.IsVisible;
			_presentationTriggers.Set("uiOpened:goalsWindow", goalsOpen ? 1 : 0);

			bool cardDrawShowing = (_cardDrawAnimator != null && _cardDrawAnimator.IsPlaying)
				|| (_cardDrawOverlay != null && _cardDrawOverlay.style.display == DisplayStyle.Flex);
			bool orgInfoOpen = _orgInfoDocument != null && _orgInfoDocument.IsVisible;
			bool leaderboardOpen = _leaderboardWindow != null && _leaderboardWindow.IsVisible;
			bool warProgressOpen = _warProgressWindow != null && _warProgressWindow.IsVisible;
			bool gameMenuOpen = _gameMenu != null && _gameMenu.IsVisible;
			bool provinceOpen = _state.MapLens.Lens == MapLens.Province
				&& _state.SelectedProvince != null
				&& _state.SelectedProvince.IsValid;
			bool modalLocked = _modalState != null && _modalState.IsLocked();

			bool chromeClear = !selectedCountryOpen
				&& !charactersOpen
				&& !actionsOpen
				&& !goalsOpen
				&& !cardDrawShowing
				&& !orgInfoOpen
				&& !leaderboardOpen
				&& !warProgressOpen
				&& !gameMenuOpen
				&& !provinceOpen
				&& !modalLocked;
			_presentationTriggers.Set("uiElementShown:none", chromeClear ? 1 : 0);

			bool militaryAdvisorTooltip = _tooltip != null
				&& _tooltip.HasVisibleTooltipWithIdPrefix("role-military_advisor-");
			_presentationTriggers.Set(
				"tooltipShown:militaryAdvisorTooltip",
				militaryAdvisorTooltip ? 1 : 0);
		}

		VisualElement ResolveHighlightTarget(string targetId) {
			if (string.IsNullOrEmpty(targetId) || _root == null) {
				return null;
			}
			switch (targetId) {
				case "player_org_panel":
					return _root.Q(className: "player-country-panel") ?? _root.Q("player-country");
				case "time_panel":
					return _root.Q("time-panel");
				case "characters_button":
					return _root.Q("chars-toggle-btn");
				case "military_advisor_card":
					return _countryInfo?.CharactersView?.FindCardByRole("military_advisor");
				case "actions_button":
					return _root.Q("actions-toggle-btn");
				case "action_deck":
					return _countryInfo?.ActionsView?.DeckPileElement
						?? _root.Q(className: "action-deck-wrapper");
				case "goals_button":
					return _btnGoals ?? _root.Q("btn-goals");
				default:
					return null;
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
		}

		void RefreshMyOrgCardAvailability() {
			if (_state == null || _myOrgCardDebug == null) {
				return;
			}
			var availability = _state.MyOrgCardAvailability;
			_myOrgCardDebug.RefreshDeck(availability.Deck);
			_myOrgCardDebug.RefreshHand(availability.Hand, GetPlayerGold());
		}

		void RefreshSelectedOrgCardAvailability() {
			if (_state == null || _selectedOrgCardDebug == null) {
				return;
			}
			var availability = _state.SelectedOrgCardAvailability;
			_selectedOrgCardDebug.RefreshDeck(availability.Deck);
			_selectedOrgCardDebug.RefreshHand(availability.Hand, GetOrgLensGold());
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
			if (_state?.Leaderboard?.Organizations != null) {
				foreach (var entry in _state.Leaderboard.Organizations) {
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
			foreach (var entry in _state.Leaderboard.Organizations) {
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

		void HandleCountryChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			RefreshSelectedCountryCharacterDebugButtons();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
			RefreshSelectedCountryDebugName();
			RefreshControlOrgDebugList();
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

		void HandlePlayerOrgChanged(object sender, PropertyChangedEventArgs e) {
			if (_state.PlayerOrganization.IsDestroyed) {
				_orgPanelOpen = false;
				_orgInfoDocument?.Hide();
			}

			RefreshCountryViews();
		}

		void HandleControlChanged(object sender, PropertyChangedEventArgs e) {
			if (_cardPlayAnimator != null && _cardPlayAnimator.IsPlaying) { return; }
			RefreshCountryViews();
			RefreshControlOrgDebugList();
		}

		void HandleCardPlayComplete() {
			_countryInfo?.ActionsView?.SetPresentationBusy(_cardDrawAnimator?.IsPlaying ?? false);
			RefreshCountryViews();
		}

		void HandleTimeChanged(object sender, PropertyChangedEventArgs e) {
			_timeView.Refresh(_state.Time);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			_loc.SetLocale(_state.Locale.Locale);
			_tooltip?.HideAll();
			_warIconsView?.Refresh(_state.WarIcons);
			_playerTasksView?.Refresh(_state.ActiveTasks);
			_tutorialHighlightView?.Refresh(_state.ActiveTasks);
			RefreshLeaderboardButtonText();
			RefreshGoalsButtonText();
			RefreshCountryViews();
			_cardDrawAnimator?.RefreshPendingOfferPresentation();
			RefreshProvinceInfoView();
			_timeView.Refresh(_state.Time);
		}

		void HandleActiveTasksChanged(object sender, PropertyChangedEventArgs e) {
			_playerTasksView?.Refresh(_state.ActiveTasks);
			_tutorialHighlightView?.Refresh(_state.ActiveTasks);
		}

		void HandleLastFrameEffectsChanged(object sender, PropertyChangedEventArgs e) {
			if (_state == null || _state.LastFrameEffects.Effects.Count == 0) { return; }
			if (_cardPlayAnimator != null && _cardPlayAnimator.IsPlaying) { return; }
			if (!_state.PlayerOrganization.IsValid) { return; }

			string playerOrgId = _state.PlayerOrganization.OrgId;
			foreach (var effect in _state.LastFrameEffects.Effects) {
				if (effect.OwnerId != playerOrgId) { continue; }
				if (effect.ResourceId != ResourceDefinitions.Gold) { continue; }
				AnimatableDouble goldAnimatable = null;
				foreach (var res in _state.PlayerOrganization.Resources.Resources) {
					if (res.ResourceId == ResourceDefinitions.Gold) { goldAnimatable = res.Value; break; }
				}
				if (goldAnimatable == null) { continue; }
				var barrier = goldAnimatable.Hold(-effect.Amount);
				barrier.Release(3.0f);
			}
		}

		void HandlePlayerResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
			RefreshMyOrgCardAvailability();
		}

		void HandleSelectedResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
		}

		void HandleMyOrgCardAvailabilityChanged(object sender, PropertyChangedEventArgs e) => RefreshMyOrgCardAvailability();

		void HandleSelectedOrgCardAvailabilityChanged(object sender, PropertyChangedEventArgs e) => RefreshSelectedOrgCardAvailability();

		void HandleOrgLensResourcesChanged(object sender, PropertyChangedEventArgs e) {
			RefreshSelectedOrgCardAvailability();
			RefreshSelectedOrgDebugName();
			RefreshSelectedOrgDebugMenuAvailability();
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

		void HandleCountryActionsChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			_cardDrawAnimator?.RestorePendingOfferIfIdle();
		}

		void HandleRelationsChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
			RebuildRelationCountryDropdown();
			RefreshRelationActionButtons();
		}

		void HandleWarsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

		void HandleCountryActionCardClicked(
			string actionId,
			string targetCountryId,
			int slotIndex,
			VisualElement element,
			ActionCardBuilder.CountryCardFace faceData) {
			if (_cardPlayAnimator == null
				|| (_cardDrawAnimator?.IsPlaying ?? false)
				|| _cardPlayAnimator.IsPlaying
				|| _state == null
				|| !_state.PlayerOrganization.IsValid
				|| !_state.SelectedCountry.IsValid) {
				return;
			}
			_countryInfo?.ActionsView?.SetPresentationBusy(true);
			_cardPlayAnimator.StartCountryCardPlay(
				_state.PlayerOrganization.OrgId,
				_state.SelectedCountry.CountryId,
				actionId,
				slotIndex,
				element,
				faceData,
				targetCountryId);
		}

		void HandleCountryActionCardDiscarded(
			string actionId,
			string targetCountryId,
			int slotIndex,
			VisualElement element,
			ActionCardBuilder.CountryCardFace faceData) {
			if (_cardDrawAnimator == null
				|| (_cardPlayAnimator?.IsPlaying ?? false)
				|| _cardDrawAnimator.IsPlaying
				|| _state == null
				|| !_state.PlayerOrganization.IsValid
				|| !_state.SelectedCountry.IsValid) {
				return;
			}
			_cardDrawAnimator.StartPaidDiscard(
				_state.PlayerOrganization.OrgId,
				_state.SelectedCountry.CountryId,
				actionId,
				slotIndex,
				element,
				faceData,
				targetCountryId);
		}

		void HandleCountryActionDrawRequested() {
			if (_cardDrawAnimator == null
				|| (_cardPlayAnimator?.IsPlaying ?? false)
				|| _cardDrawAnimator.IsPlaying) {
				return;
			}
			_cardDrawAnimator.StartExplicitDraw();
		}

		void HandleUnplayableCountryActionCardReleased(ActionConditionDebugEntry condition) {
			if (condition == null) {
				return;
			}
			_flyText?.Notify(
				condition.LocaleKey,
				ActionConditionText.ToLocalizedFormatArguments(_loc, condition));
		}

		void HandleCountryActionCardDiscardUnaffordable() {
			_flyText?.Notify("action.discard.no_gold");
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

		void HandleLeaderboardChanged(object sender, PropertyChangedEventArgs e) {
			RebuildControlOrgDropdown();
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
			if (_state.PlayerOrganization.IsDestroyed) {
				_orgPanelOpen = false;
				_orgInfoDocument.Hide();
				return;
			}
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
