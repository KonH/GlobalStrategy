using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class GoalsWindowDocument : MonoBehaviour {
		VisualState _state;
		GameLogic _gameLogic;
		ILocalization _loc;
		OrgVisualConfig _orgVisualConfig;
		UIDocument _doc;
		VisualElement _root;
		Label _title;
		Button _closeButton;
		GoalsWindowView _view;
		ModalState _modalState;
		readonly LeaderboardState _leaderboard = new LeaderboardState();
		readonly GoalsState _goals = new GoalsState();
		readonly PullRefreshTimer _refreshTimer = new PullRefreshTimer();
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, GameLogic gameLogic, ILocalization loc, OrgVisualConfig orgVisualConfig, ModalState modalState) {
			_state = state;
			_gameLogic = gameLogic;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_modalState = modalState;
		}

		// Explicit sortingOrder, not scene-authoring order — see .claude/rules/unity/uitoolkit.md
		// Distinct from Leaderboard (500) and WarProgress (510); below FlyText (1000).
		const int SortingOrder = 505;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_title = _root.Q<Label>("goals-title");
			_closeButton = _root.Q<Button>("btn-close");
			_closeButton?.OnClick(Hide);
			Hide();
		}

		void Start() {
			EnsureView();
			Subscribe();
			RefreshTexts();
		}

		void OnEnable() {
			Subscribe();
		}

		void OnDisable() {
			Unsubscribe();
		}

		void Update() {
			if (!IsVisible || _gameLogic == null) {
				return;
			}
			if (_refreshTimer.ShouldRefresh(Time.deltaTime, _state.Time.IsPaused)) {
				RefreshGoals();
			}
		}

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		public void Show() {
			if (_root == null || _state == null) {
				return;
			}
			EnsureView();
			RefreshTexts();
			_view?.ResetToPlayerOrg(_state.PlayerOrganization.OrgId);
			_refreshTimer.RequestImmediate();
			RefreshGoals();
			if (IsVisible) {
				return;
			}
			_modalState.Lock(this);
			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
			_modalState.Unlock(this);
		}

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_state.PlayerOrganization.PropertyChanged += HandlePlayerOrganizationChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_state.PlayerOrganization.PropertyChanged -= HandlePlayerOrganizationChanged;
			_subscribed = false;
		}

		void HandleLocaleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			// HUD owns locale switching — only refresh bound texts/view here.
			RefreshTexts();
			if (IsVisible) {
				RefreshGoals();
			}
		}

		void HandlePlayerOrganizationChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			if (IsVisible) {
				RefreshGoals();
			}
		}

		void RefreshGoals() {
			if (_gameLogic == null) {
				return;
			}
			LeaderboardProjector.Project(_gameLogic.World, _leaderboard, _gameLogic.Resources, _gameLogic.CountryConfig);
			_goals.Set(GoalsProjector.Project(_gameLogic.World, _gameLogic.GameSettings.CompletionCondition, _gameLogic.MaxControlPool, _gameLogic.Resources));
			_view?.Refresh(_leaderboard, _goals);
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new GoalsWindowView(_root, _loc, _orgVisualConfig);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}

		void RefreshTexts() {
			if (_title != null) {
				_title.text = GetText("goals.title", "Goals");
			}
		}
	}
}
