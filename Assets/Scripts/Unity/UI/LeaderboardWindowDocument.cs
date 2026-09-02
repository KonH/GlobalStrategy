using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class LeaderboardWindowDocument : MonoBehaviour {
		VisualState _state;
		GameLogic _gameLogic;
		ILocalization _loc;
		CountryVisualConfig _countryVisualConfig;
		OrgVisualConfig _orgVisualConfig;
		UIDocument _doc;
		VisualElement _root;
		Label _title;
		Button _closeButton;
		Button _tabOrganizations;
		Button _tabCountries;
		Label _empty;
		LeaderboardWindowView _view;
		ModalState _modalState;
		readonly LeaderboardState _leaderboard = new LeaderboardState();
		readonly PullRefreshTimer _refreshTimer = new PullRefreshTimer();
		bool _localeSubscribed;

		[Inject]
		void Construct(VisualState state, GameLogic gameLogic, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig, ModalState modalState) {
			_state = state;
			_gameLogic = gameLogic;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_modalState = modalState;
		}

		// Explicit sortingOrder, not scene-authoring order — see .claude/rules/unity/uitoolkit.md
		// "Layer Model" (sortingOrder governs stacking among documents sharing HUDPanelSettings;
		// below FlyTextNotifierDocument's 1000 so fly-text still renders above this modal).
		const int SortingOrder = 500;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_title = _root.Q<Label>("leaderboard-title");
			_closeButton = _root.Q<Button>("btn-close");
			_tabOrganizations = _root.Q<Button>("tab-organizations");
			_tabCountries = _root.Q<Button>("tab-countries");
			_empty = _root.Q<Label>("leaderboard-empty");
			if (_closeButton != null) {
				_closeButton.OnClick(Hide);
			}
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
				RefreshLeaderboard();
			}
		}

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		public void Show() {
			if (_root == null || _state == null) {
				return;
			}
			EnsureView();
			RefreshTexts();
			_refreshTimer.RequestImmediate();
			if (IsVisible) {
				RefreshLeaderboard();
				return;
			}
			_modalState.Lock(this);
			_view?.ResetToDefaultTab();
			RefreshLeaderboard();
			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
			_modalState.Unlock(this);
		}

		void Subscribe() {
			if (_localeSubscribed || _state == null) {
				return;
			}
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_localeSubscribed = true;
		}

		void Unsubscribe() {
			if (!_localeSubscribed || _state == null) {
				return;
			}
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_localeSubscribed = false;
		}

		void HandleLocaleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			RefreshTexts();
			if (IsVisible) {
				RefreshLeaderboard();
			}
		}

		void RefreshLeaderboard() {
			if (_gameLogic == null) {
				return;
			}
			LeaderboardProjector.Project(_gameLogic.World, _leaderboard, _gameLogic.Resources, _gameLogic.CountryConfig);
			_view?.Refresh(_leaderboard);
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new LeaderboardWindowView(_root, _loc, _countryVisualConfig, _orgVisualConfig);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}

		void RefreshTexts() {
			if (_title != null) {
				_title.text = GetText("leaderboard.title", "Leaderboard");
			}
			if (_tabOrganizations != null) {
				_tabOrganizations.text = GetText("leaderboard.tab.organizations", "Organizations");
			}
			if (_tabCountries != null) {
				_tabCountries.text = GetText("leaderboard.tab.countries", "Countries");
			}
			if (_empty != null) {
				_empty.text = GetText("leaderboard.empty", "No leaderboard entries available");
			}
		}
	}
}
