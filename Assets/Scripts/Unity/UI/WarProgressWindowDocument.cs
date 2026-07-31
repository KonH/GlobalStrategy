using System.ComponentModel;
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
		ILocalization _loc;
		CountryVisualConfig _countryVisualConfig;
		UIDocument _doc;
		VisualElement _root;
		WarProgressWindowView _view;
		TooltipSystem _tooltip;
		bool _ownsModalState;
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, ILocalization loc, CountryVisualConfig countryVisualConfig) {
			_state = state;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
		}

		const int SortingOrder = 510;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_tooltip = new TooltipSystem(_root);
			Button closeButton = _root.Q<Button>("btn-close");
			closeButton?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && closeButton.ContainsPoint(e.localPosition)) {
					Hide();
				}
			});
			Hide();
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
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
			if (IsVisible) {
				_view?.Refresh(_state.SelectedWar);
				return;
			}
			ModalState.IsModalOpen = true;
			_ownsModalState = true;
			_root.style.display = DisplayStyle.Flex;
			_view?.Refresh(_state.SelectedWar);
		}

		public void Hide() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
			if (_ownsModalState) {
				ModalState.IsModalOpen = false;
				_ownsModalState = false;
			}
			_state?.SelectedWar.Clear();
		}

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}
			_state.SelectedWar.PropertyChanged += HandleSelectedWarChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.SelectedWar.PropertyChanged -= HandleSelectedWarChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_subscribed = false;
		}

		void HandleSelectedWarChanged(object sender, PropertyChangedEventArgs e) {
			if (!IsVisible) {
				return;
			}
			if (!_state.SelectedWar.IsValid) {
				Hide();
				return;
			}
			_view?.Refresh(_state.SelectedWar);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
			if (IsVisible) {
				_view?.Refresh(_state.SelectedWar);
			}
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new WarProgressWindowView(_root, _loc, _countryVisualConfig, _tooltip);
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
