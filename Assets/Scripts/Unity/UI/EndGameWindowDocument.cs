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
		GameSettings _gameSettings;
		ILocalization _loc;
		OrgVisualConfig _orgVisualConfig;
		SceneLoader _sceneLoader;
		UIDocument _doc;
		VisualElement _root;
		Button _btnExit;
		EndGameWindowView _view;
		bool _ownsModalState;
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, GameSettings gameSettings, ILocalization loc, OrgVisualConfig orgVisualConfig, SceneLoader sceneLoader) {
			_state = state;
			_gameSettings = gameSettings;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
			_sceneLoader = sceneLoader;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = _sortingOrder;
			_root = _doc.rootVisualElement;
			_btnExit = _root.Q<Button>("btn-exit");
			_btnExit?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnExit.ContainsPoint(e.localPosition)) {
					_sceneLoader.LoadMainMenu();
				}
			});
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

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}
			_state.GameCompletion.PropertyChanged += HandleStateChanged;
			_state.Leaderboard.PropertyChanged += HandleStateChanged;
			_state.PlayerOrganization.PropertyChanged += HandleStateChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.GameCompletion.PropertyChanged -= HandleStateChanged;
			_state.Leaderboard.PropertyChanged -= HandleStateChanged;
			_state.PlayerOrganization.PropertyChanged -= HandleStateChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
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
				if (_ownsModalState) {
					ModalState.IsModalOpen = false;
					_ownsModalState = false;
				}
				_root.style.display = DisplayStyle.None;
				return;
			}
			ModalState.IsModalOpen = true;
			_ownsModalState = true;
			_root.style.display = DisplayStyle.Flex;
			_view.Refresh(_state.GameCompletion, _state.Leaderboard, _state.PlayerOrganization, _gameSettings.EndGameComparisons);
		}

		void RefreshTexts() {
			if (_btnExit != null) {
				_btnExit.text = _loc.Get("end_game.exit");
			}
		}
	}
}
