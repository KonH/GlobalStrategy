using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class WarProgressWindowDocument : MonoBehaviour {
		VisualState _state;
		GameLogic _gameLogic;
		ILocalization _loc;
		CountryVisualConfig _countryVisualConfig;
		UIDocument _doc;
		VisualElement _root;
		WarProgressWindowView _view;
		TooltipSystem _tooltip;
		ModalState _modalState;
		readonly PullRefreshTimer _refreshTimer = new PullRefreshTimer();
		bool _localeSubscribed;

		[Inject]
		void Construct(VisualState state, GameLogic gameLogic, ILocalization loc, CountryVisualConfig countryVisualConfig, ModalState modalState) {
			_state = state;
			_gameLogic = gameLogic;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_modalState = modalState;
		}

		const int SortingOrder = 510;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_tooltip = new TooltipSystem(_root);
			Button closeButton = _root.Q<Button>("btn-close");
			closeButton?.OnClick(Hide);
			Hide();
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
			if (!IsVisible || _gameLogic == null) {
				return;
			}
			if (_refreshTimer.ShouldRefresh(Time.deltaTime, _state.Time.IsPaused)) {
				RefreshSelectedWar();
			}
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

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		public void Open(string warId) {
			if (_root == null || _state == null) {
				return;
			}
			EnsureView();
			_state.SelectedWar.RequestOpen(warId);
			RefreshTexts();
			_refreshTimer.RequestImmediate();
			RefreshSelectedWar();
			if (IsVisible) {
				return;
			}
			if (!_state.SelectedWar.IsValid) {
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
			_state?.SelectedWar.Clear();
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
				RefreshSelectedWar();
			}
		}

		void RefreshSelectedWar() {
			if (_gameLogic == null) {
				return;
			}
			SelectedWarProjector.Project(_gameLogic.World, _state.SelectedWar, _gameLogic.Resources, _gameLogic.CountryConfig);
			if (IsVisible && !_state.SelectedWar.IsValid) {
				Hide();
				return;
			}
			_view?.Refresh(_state.SelectedWar);
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new WarProgressWindowView(_root, _loc, _countryVisualConfig, _gameLogic.EffectConfig, _tooltip);
		}

		void RefreshTexts() {
			_view?.RefreshStaticTexts(GetText);
		}

		string GetText(string key, string fallback) {
			string value = _loc?.Get(key) ?? "";
			return string.IsNullOrEmpty(value) || value == key ? fallback : value;
		}
	}
}
