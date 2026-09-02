using System.ComponentModel;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class OrgDestroyedWindowDocument : MonoBehaviour {
		const int SortingOrder = 515;

		VisualState _state;
		ILocalization _loc;
		UIDocument _doc;
		VisualElement _root;
		OrgDestroyedWindowView _view;
		ModalState _modalState;
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, ILocalization loc, ModalState modalState) {
			_state = state;
			_loc = loc;
			_modalState = modalState;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_modalState.Unlocked += HandleModalUnlocked;

			Button closeButton = _root.Q<Button>("btn-close");
			closeButton?.OnClick(Hide);

			Button confirmButton = _root.Q<Button>("btn-confirm");
			confirmButton?.OnClick(Hide);

			HideVisualOnly();
		}

		void Start() {
			EnsureView();
			Subscribe();
			RefreshTexts();
			TryOpenIfQueued();
		}

		void OnEnable() {
			Subscribe();
		}

		void OnDisable() {
			Unsubscribe();
		}

		void OnDestroy() {
			if (_modalState != null) {
				_modalState.Unlocked -= HandleModalUnlocked;
			}
		}

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		void OpenCurrent() {
			if (_root == null || _state == null) {
				return;
			}
			if (!_state.OrgDestroyedResults.TryPeek(out OrgDestroyedSnapshotState snapshot)
				|| snapshot == null) {
				return;
			}

			EnsureView();
			_modalState.Lock(this);
			_root.style.display = DisplayStyle.Flex;
			RefreshTexts();
			_view?.Refresh(snapshot);
		}

		public void Hide() {
			HideVisualOnly();
			_state?.OrgDestroyedResults.AcknowledgeCurrent();
			_modalState.Unlock(this);
		}

		void HideVisualOnly() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
		}

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}

			_state.OrgDestroyedResults.PropertyChanged += HandleOrgDestroyedResultsChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}

			_state.OrgDestroyedResults.PropertyChanged -= HandleOrgDestroyedResultsChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_subscribed = false;
		}

		void HandleOrgDestroyedResultsChanged(object sender, PropertyChangedEventArgs e) {
			TryOpenIfQueued();
		}

		void TryOpenIfQueued() {
			if (IsVisible) {
				return;
			}
			if (_state == null || !_state.OrgDestroyedResults.TryPeek(out _)) {
				return;
			}
			if (_modalState.IsLocked()) {
				return;
			}

			OpenCurrent();
		}

		void HandleModalUnlocked() {
			TryOpenIfQueued();
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
			if (IsVisible
				&& _state != null
				&& _state.OrgDestroyedResults.TryPeek(out OrgDestroyedSnapshotState snapshot)) {
				_view?.Refresh(snapshot);
			}
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}

			_view = new OrgDestroyedWindowView(_root, _loc);
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
