using System.Collections.Generic;
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Configs;
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
		OrgInfoDocument _orgInfoDocument;
		VisualElement _root;
		VisualElement _countryInfoRoot;
		VisualElement _provinceInfoRoot;
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
		UIPointerState _pointerState;
		CountryActionsVisibility _countryActionsVisibility;
		GameLogic _gameLogic;
		TutorialPresentationTriggers _presentationTriggers;
		TutorialHighlightView _tutorialHighlightView;
		VisualElement _cardDrawOverlay;
		List<IHudPanelBinder> _binders;

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, GameMenuDocument gameMenu, LeaderboardWindowDocument leaderboardWindow, GoalsWindowDocument goalsWindow, WarProgressWindowDocument warProgressWindow, OrgInfoDocument orgInfoDocument, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator, CountryConfig countryConfig, IFlyTextNotifier flyText, GameSettings gameSettings, UIPointerState pointerState, ModalState modalState, CountryActionsVisibility countryActionsVisibility, GameLogic gameLogic, TutorialPresentationTriggers presentationTriggers) {
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
			_gameLogic = gameLogic;
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
				playerOrgRoot.OnClick(ToggleOrgInfo);
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
				_btnMenu.OnClick(() => _gameMenu?.Show());
			}
			if (_btnLeaderboard != null) {
				_btnLeaderboard.OnClick(() => _leaderboardWindow?.Show());
				RefreshLeaderboardButtonText();
			}
			if (_btnGoals != null) {
				_btnGoals.OnClick(() => _goalsWindow?.Show());
				RefreshGoalsButtonText();
			}

			_binders = BuildBinders();

			_started = true;
			SubscribeViewEvents();
			_countryInfo.ActionsView?.SetPresentationBusy(_cardPlayAnimator?.IsPlaying ?? false);
			RefreshCountryViews();
			_cardDrawAnimator?.SetRestorationEnabled(true);
			_cardDrawAnimator?.RestorePendingOfferIfIdle();
		}

		// One binder per HUD panel's VisualState slice (Docs/Specs/26_08_28_16_ui-refactoring
		// phase 7) - each owns only its own PropertyChanged subscribe/unsubscribe pair(s). Shared
		// multi-view refreshes (RefreshCountryViews/RefreshProvinceInfoView) and any extra
		// side-effecting logic stay as HUDDocument methods/lambdas passed in as delegates, since
		// they read/write fields (_orgPanelOpen, _lastNotifiedLogSequenceId, ...) that several
		// panels share.
		List<IHudPanelBinder> BuildBinders() {
			return new List<IHudPanelBinder> {
				new SelectedCountryBinder(_state.SelectedCountry, RefreshCountryViews),
				new PlayerOrganizationBinder(_state.PlayerOrganization, CloseOrgPanel, RefreshCountryViews),
				new TimeBinder(_state.Time, _timeView),
				new LocaleBinder(_state.Locale, HandleLocaleChanged),
				new ResourcesBinder(_state, _playerOrgView, _countryInfo, _orgLensCountryView),
				new ControlBinder(_state.SelectedCountry.Control, _countryInfo, () => _cardPlayAnimator?.IsPlaying ?? false, RefreshCountryViews),
				new CharactersBinder(_state.SelectedCountry.Characters, RefreshCountryViews),
				new CountryActionsBinder(_state.SelectedCountry.CountryActions, RefreshCountryViews, () => _cardDrawAnimator?.RestorePendingOfferIfIdle()),
				new RelationsBinder(_state.SelectedCountry.Relations, RefreshCountryViews),
				new WarsBinder(_state.SelectedCountry.Wars, RefreshCountryViews),
				new MapLensBinder(_state.MapLens, _lensSwitcher, RefreshCountryViews, RefreshProvinceInfoView),
				new OrgMapBinder(_state.OrgMap, RefreshCountryViews),
				new SelectedProvinceBinder(_state.SelectedProvince, RefreshProvinceInfoView),
				new ProvinceOwnershipBinder(_state.ProvinceOwnership, RefreshProvinceInfoView),
				new ProvinceOccupationBinder(_state.ProvinceOccupation, RefreshProvinceInfoView),
				new GameLogBinder(_state.GameLog, _actionLog, NotifyNewLogEntries),
				new WarIconsBinder(_state.WarIcons, _warIconsView),
				new ActiveTasksBinder(_state.ActiveTasks, _playerTasksView, _tutorialHighlightView),
				new LastFrameEffectsBinder(_state, () => _cardPlayAnimator?.IsPlaying ?? false, _root, RefreshCountryViews),
			};
		}

		void CloseOrgPanel() {
			_orgPanelOpen = false;
			_orgInfoDocument?.Hide();
		}

		void OnEnable() {
			int enableGeneration = ++_enableGeneration;
			if (_state == null) {
				return;
			}
			if (_cardPlayAnimator != null) { _cardPlayAnimator.OnCardPlayComplete += HandleCardPlayComplete; }
			if (_binders != null) {
				foreach (var binder in _binders) {
					binder.Subscribe();
				}
			}
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
			_warIconsView?.Refresh(_state.WarIcons);
			_playerTasksView?.Refresh(_state.ActiveTasks);
			_tutorialHighlightView?.Refresh(_state.ActiveTasks);
			RefreshCountryViews();
			RefreshProvinceInfoView();
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
			if (_binders != null) {
				foreach (var binder in _binders) {
					binder.Unsubscribe();
				}
			}
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
			if (_orgPanelOpen) {
				var mouse = UnityEngine.InputSystem.Mouse.current;
				if (mouse != null && mouse.leftButton.wasPressedThisFrame) {
					if (!_pointerState.IsPointerOverUI(mouse.position.ReadValue())) {
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

		void HandleCardPlayComplete() {
			_countryInfo?.ActionsView?.SetPresentationBusy(_cardDrawAnimator?.IsPlaying ?? false);
			RefreshCountryViews();
		}

		void HandleLocaleChanged() {
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

		string GetProvinceOwner(string provinceId) {
			if (_state == null || string.IsNullOrEmpty(provinceId)) { return ""; }
			return _state.ProvinceOwnership.OwnerByProvinceId.TryGetValue(provinceId, out var ownerId) ? ownerId : "";
		}

		string GetProvinceOccupier(string provinceId) {
			if (_state == null || string.IsNullOrEmpty(provinceId)) { return ""; }
			return _state.ProvinceOccupation.OccupierByProvinceId.TryGetValue(provinceId, out var occupierId) ? occupierId : "";
		}

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

		void HandleOrgSubPanelOpened(bool anyOpen) {
			var lensSwitcherEl = _root.Q("lens-switcher");
			if (lensSwitcherEl != null) {
				lensSwitcherEl.style.display = anyOpen ? DisplayStyle.None : DisplayStyle.Flex;
			}
			if (!anyOpen) {
				_tooltip?.HideAll();
			}
		}
	}
}
