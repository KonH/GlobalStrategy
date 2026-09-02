using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class EndGameWindowDocument : MonoBehaviour {
		[SerializeField] int _sortingOrder = 1100;

		VisualState _state;
		GameLogic _gameLogic;
		GameSettings _gameSettings;
		ILocalization _loc;
		OrgVisualConfig _orgVisualConfig;
		SceneLoader _sceneLoader;
		UIDocument _doc;
		VisualElement _root;
		Button _btnExit;
		EndGameWindowView _view;
		ModalState _modalState;
		readonly LeaderboardState _leaderboard = new LeaderboardState();
		readonly PullRefreshTimer _refreshTimer = new PullRefreshTimer();
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, GameLogic gameLogic, GameSettings gameSettings, ILocalization loc, OrgVisualConfig orgVisualConfig, SceneLoader sceneLoader, ModalState modalState) {
			_state = state;
			_gameLogic = gameLogic;
			_gameSettings = gameSettings;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_sceneLoader = sceneLoader;
			_modalState = modalState;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = _sortingOrder;
			_root = _doc.rootVisualElement;
			_btnExit = _root.Q<Button>("btn-exit");
			if (_btnExit != null) {
				_btnExit.OnClick(() => _sceneLoader.LoadMainMenu());
			}
			_root.style.display = DisplayStyle.None;
		}

		void Start() {
			_view = new EndGameWindowView(_root, _loc, _orgVisualConfig);
			RefreshTexts();
			HandleStateChanged(null, null);
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
				RefreshView();
			}
		}

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}
			_state.GameCompletion.PropertyChanged += HandleStateChanged;
			_state.PlayerOrganization.PropertyChanged += HandleStateChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_modalState.Unlocked += HandleModalUnlocked;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.GameCompletion.PropertyChanged -= HandleStateChanged;
			_state.PlayerOrganization.PropertyChanged -= HandleStateChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_modalState.Unlocked -= HandleModalUnlocked;
			_subscribed = false;
		}

		void HandleLocaleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			RefreshTexts();
			HandleStateChanged(sender, e);
		}

		void HandleStateChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			if (_state == null || _view == null) {
				return;
			}
			if (!_state.GameCompletion.IsCompleted) {
				_modalState.Unlock(this);
				_root.style.display = DisplayStyle.None;
				return;
			}
			if (IsVisible) {
				_refreshTimer.RequestImmediate();
				RefreshView();
				return;
			}

			TryOpenIfQueued();
		}

		bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		void TryOpenIfQueued() {
			if (IsVisible || _state == null || !_state.GameCompletion.IsCompleted) {
				return;
			}
			if (_state.OrgDestroyedResults.TryPeek(out _)) {
				return;
			}
			if (_modalState.IsLocked()) {
				return;
			}

			OpenCurrent();
		}

		void OpenCurrent() {
			_modalState.Lock(this);
			_root.style.display = DisplayStyle.Flex;
			_refreshTimer.RequestImmediate();
			RefreshView();
		}

		void RefreshView() {
			if (_gameLogic != null) {
				LeaderboardProjector.Project(_gameLogic.World, _leaderboard, _gameLogic.Resources, _gameLogic.CountryConfig);
			}
			_view.Refresh(_state.GameCompletion, _leaderboard, _state.PlayerOrganization, _gameSettings.EndGameComparisons);
		}

		void HandleModalUnlocked() {
			TryOpenIfQueued();
		}

		void RefreshTexts() {
			if (_btnExit != null) {
				_btnExit.text = _loc.Get("end_game.exit");
			}
		}
	}
}
