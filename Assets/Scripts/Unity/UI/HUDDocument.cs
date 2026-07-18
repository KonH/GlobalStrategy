using System.Collections.Generic;
using System.ComponentModel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Unity.EcsViewer;
using GS.Unity.Common;
using GS.Unity.Map;

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
		CountryVisualConfig _countryVisualConfig;
		OrgVisualConfig _orgVisualConfig;
		GameMenuDocument _gameMenu;
		Button _btnMenu;
		Button _btnDebugToggle;
		VisualElement _debugPanel;
		Button _btnEcsViewer;
		VisualElement _controlDebugRow;
		bool _debugPanelOpen;
		OrgInfoDocument _orgInfoDocument;
		VisualElement _root;
		VisualElement _countryInfoRoot;
		int _lastOrgAgentSlotCount = -1;
		bool _orgPanelOpen;
		LensSwitcherView _lensSwitcher;
		OrgLensCountryView _orgLensCountryView;
		ActionConfig _actionConfig;
		ActionVisualConfig _actionVisualConfig;
		CardPlayAnimator _cardPlayAnimator;
		Button _btnReassignProvince;
		readonly List<Button> _selectedCountryCharacterDebugButtons = new();

		[Inject]
		void Construct(VisualState state, IWriteOnlyCommandAccessor commands, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, GameMenuDocument gameMenu, OrgInfoDocument orgInfoDocument, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator) {
			_state = state;
			_commands = commands;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_gameMenu = gameMenu;
			_orgInfoDocument = orgInfoDocument;
			_actionConfig = actionConfig;
			_actionVisualConfig = actionVisualConfig;
			_cardPlayAnimator = cardPlayAnimator;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			_root = _document.rootVisualElement;
			var root = _root;

			_tooltip = new TooltipSystem(root.Q("hud-root"));
			_timeView = new TimeView(
				root.Q("time-panel"),
				OnPauseToggle,
				OnSpeedChange);
			_orgLensCountryView = new OrgLensCountryView(root.Q("org-lens-country-info"), _orgVisualConfig);

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
			_playerOrgView = new PlayerOrgView(_root.Q("player-country"), _loc, _resourceConfig, _tooltip, _orgVisualConfig);
			_lensSwitcher = new LensSwitcherView(_root.Q("lens-switcher"), _tooltip, _loc);
			_lensSwitcher.OnLensSelected = OnLensSelected;
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened += HandleOrgSubPanelOpened;
			}
			_cardPlayAnimator?.SetCountryActionsView(_countryInfo.ActionsView);
			var root = _document.rootVisualElement;
			_btnMenu = root.Q<Button>("btn-menu");
			_btnMenu.clicked += () => _gameMenu?.Show();

			_btnDebugToggle = root.Q<Button>("btn-debug-toggle");
			_debugPanel = root.Q("debug-panel");
			_btnEcsViewer = root.Q<Button>("btn-ecs-viewer");

			_btnDebugToggle.clicked += ToggleDebugPanel;
			_btnEcsViewer.clicked += OpenEcsViewer;
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
			if (characterDebugContainer != null && _characterConfig != null) {
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

				var reassignProvinceBtn = new Button(() => PushReassignProvinceCommand());
				reassignProvinceBtn.text = "Reassign Province to My HQ";
				reassignProvinceBtn.AddToClassList("gs-btn");
				reassignProvinceBtn.AddToClassList("gs-btn--small");
				reassignProvinceBtn.AddToClassList("debug-panel-button");
				characterDebugContainer.Add(reassignProvinceBtn);
				_btnReassignProvince = reassignProvinceBtn;
				RefreshProvinceCheatButton();

				var discoverAllBtn = new Button(() => PushDiscoverAllCountriesCommand());
				discoverAllBtn.text = "Discover All Countries";
				discoverAllBtn.AddToClassList("gs-btn");
				discoverAllBtn.AddToClassList("gs-btn--small");
				discoverAllBtn.AddToClassList("debug-panel-button");
				characterDebugContainer.Add(discoverAllBtn);
			}

			RebuildOrgCharDebugButtons();
			RefreshSelectedCountryCharacterDebugButtons();
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
			if (_cardPlayAnimator != null) { _cardPlayAnimator.OnCardPlayComplete += HandleCardPlayComplete; }
			_state.SelectedCountry.PropertyChanged    += HandleCountryChanged;
			_state.PlayerOrganization.PropertyChanged += HandlePlayerOrgChanged;
			_state.Time.PropertyChanged               += HandleTimeChanged;
			_state.Locale.PropertyChanged             += HandleLocaleChanged;
			_state.PlayerOrganization.Resources.PropertyChanged    += HandlePlayerResourcesChanged;
			_state.SelectedCountry.Resources.PropertyChanged  += HandleSelectedResourcesChanged;
			_state.SelectedCountry.Control.PropertyChanged  += HandleControlChanged;
			_state.SelectedCountry.Characters.PropertyChanged += HandleCharactersChanged;
			_state.SelectedCountry.CountryActions.PropertyChanged += HandleCountryActionsChanged;
			_state.MapLens.PropertyChanged            += HandleLensChanged;
			_state.OrgMap.PropertyChanged             += HandleOrgMapChanged;
			_state.PlayerOrganization.Characters.PropertyChanged += HandleOrgCharactersChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged += HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged += HandleSelectedProvinceChanged;
			_lensSwitcher?.Refresh(_state.MapLens.Lens);
			RefreshCountryViews();
			RefreshControlDebugRow();
			RefreshSelectedCountryCharacterDebugButtons();
			RefreshProvinceCheatButton();
			_timeView.Refresh(_state.Time);
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
			_state.SelectedCountry.Control.PropertyChanged  -= HandleControlChanged;
			_state.SelectedCountry.Characters.PropertyChanged -= HandleCharactersChanged;
			_state.SelectedCountry.CountryActions.PropertyChanged -= HandleCountryActionsChanged;
			_state.MapLens.PropertyChanged            -= HandleLensChanged;
			_state.OrgMap.PropertyChanged             -= HandleOrgMapChanged;
			_state.PlayerOrganization.Characters.PropertyChanged -= HandleOrgCharactersChanged;
			_state.SelectedCountry.Control.UsedControl.PropertyChanged -= HandleControlTickChanged;
			_state.SelectedProvince.PropertyChanged -= HandleSelectedProvinceChanged;
			_lastOrgAgentSlotCount = -1;
			if (_orgInfoDocument != null) {
				_orgInfoDocument.OnSubPanelOpened -= HandleOrgSubPanelOpened;
			}
			if (_countryInfo != null) { _countryInfo.OnSubPanelOpened -= HandleOrgSubPanelOpened; }
			if (_countryInfo != null) { _countryInfo.OnCountryActionCardClicked -= HandleCountryActionCardClicked; }
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
				return;
			}
			if (isOrgLens) {
				if (_countryInfoRoot != null) {
					_countryInfoRoot.style.display = DisplayStyle.None;
				}
				_orgLensCountryView?.Refresh(_state.SelectedCountry, _state.OrgMap, _state.SelectedCountry.Control);
			} else {
				_orgLensCountryView?.Hide();
				_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
				if (_orgPanelOpen && _countryInfoRoot != null) {
					_countryInfoRoot.style.display = DisplayStyle.None;
				}
			}
			_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
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
		}

		void RefreshSelectedCountryCharacterDebugButtons() {
			bool countrySelected = _state != null && _state.SelectedCountry.IsValid;
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
			RefreshCountryViews();
			_timeView.Refresh(_state.Time);
		}

		void HandlePlayerResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
		}

		void HandleSelectedResourcesChanged(object sender, PropertyChangedEventArgs e) {
			_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
		}

		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) {
			RefreshCountryViews();
		}

		void HandleCountryActionsChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

		void HandleCountryActionCardClicked(string actionId, string targetCharId, VisualElement el) {
			if (_cardPlayAnimator == null || _state == null || !_state.PlayerOrganization.IsValid || !_state.SelectedCountry.IsValid) { return; }
			_cardPlayAnimator.StartCountryCardPlay(
				_state.PlayerOrganization.OrgId,
				_state.SelectedCountry.CountryId,
				actionId, el);
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
			RefreshProvinceCheatButton();
		}

		void HandleSelectedProvinceChanged(object sender, PropertyChangedEventArgs e) {
			RefreshProvinceCheatButton();
		}

		void RefreshProvinceCheatButton() {
			if (_btnReassignProvince == null || _state == null) { return; }
			bool show = _state.MapLens.Lens == MapLens.Province && _state.SelectedProvince.IsValid;
			_btnReassignProvince.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void PushReassignProvinceCommand() {
			if (_state == null || _commands == null) { return; }
			string provinceId = _state.SelectedProvince.ProvinceId;
			string hqCountryId = _state.PlayerOrganization.HqCountryId;
			if (string.IsNullOrEmpty(provinceId) || string.IsNullOrEmpty(hqCountryId)) { return; }
			_commands.Push(new DebugChangeProvinceOwnerCommand { ProvinceId = provinceId, NewOwnerId = hqCountryId });
		}

		void PushDiscoverAllCountriesCommand() {
			_commands?.Push(new DebugDiscoverAllCountriesCommand());
		}

		void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) => RefreshCountryViews();

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
