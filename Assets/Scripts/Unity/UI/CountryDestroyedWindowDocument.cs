using System.ComponentModel;
using GS.Game.Commands;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class CountryDestroyedWindowDocument : MonoBehaviour {
		VisualState _state;
		ILocalization _loc;
		IWriteOnlyCommandAccessor _commands;
		UIDocument _doc;
		VisualElement _root;
		CountryDestroyedWindowView _view;
		ModalState _modalState;
		bool _subscribed;

		[Inject]
		void Construct(
			VisualState state,
			ILocalization loc,
			IWriteOnlyCommandAccessor commands,
			ModalState modalState) {
			_state = state;
			_loc = loc;
			_commands = commands;
			_modalState = modalState;
		}

		// Explicit sortingOrder — same band as WarResult (510), below EndGame.
		const int SortingOrder = 515;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_modalState.Unlocked += HandleModalUnlocked;
			Button closeButton = _root.Q<Button>("btn-close");
			closeButton?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && closeButton.ContainsPoint(e.localPosition)) {
					Hide();
				}
			});
			Button confirmButton = _root.Q<Button>("btn-confirm");
			confirmButton?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && confirmButton.ContainsPoint(e.localPosition)) {
					Hide();
				}
			});
			HideVisualOnly();
		}

		void Start() {
			EnsureView();
			Subscribe();
			RefreshTexts();
			DeselectIfDestroyedSelected();
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
			if (!_state.CountryDestroyedResults.TryPeek(out CountryDestroyedSnapshotState snapshot)
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
			// Unlock fires ModalState.Unlocked → both this and any other modal window (e.g.
			// WarResultWindowDocument) re-check whether they can open now.
			_modalState.Unlock(this);

			// AcknowledgeCurrent raises PropertyChanged → TryOpenIfQueued → OpenCurrent for the
			// next FIFO item; do not call OpenCurrent again here.
			_state?.CountryDestroyedResults.AcknowledgeCurrent();
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
			_state.CountryDestroyedResults.PropertyChanged += HandleCountryDestroyedResultsChanged;
			_state.WorldCountries.PropertyChanged += HandleWorldCountriesChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.CountryDestroyedResults.PropertyChanged -= HandleCountryDestroyedResultsChanged;
			_state.WorldCountries.PropertyChanged -= HandleWorldCountriesChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_subscribed = false;
		}

		void HandleCountryDestroyedResultsChanged(object sender, PropertyChangedEventArgs e) {
			DeselectIfDestroyedSelected();
			TryOpenIfQueued();
		}

		void HandleWorldCountriesChanged(object sender, PropertyChangedEventArgs e) {
			DeselectIfDestroyedSelected();
		}

		void TryOpenIfQueued() {
			if (IsVisible) {
				return;
			}
			if (_state == null || !_state.CountryDestroyedResults.TryPeek(out _)) {
				return;
			}
			// Any other modal window (e.g. WarResultWindow) currently holding the lock keeps this
			// one queued until it closes — mutual exclusion between modal windows by default.
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
				&& _state.CountryDestroyedResults.TryPeek(out CountryDestroyedSnapshotState snapshot)) {
				_view?.Refresh(snapshot);
			}
		}

		void DeselectIfDestroyedSelected() {
			if (_state == null || _commands == null) {
				return;
			}
			SelectedCountryState selected = _state.SelectedCountry;
			if (selected == null || !selected.IsValid) {
				return;
			}
			string countryId = selected.CountryId;
			if (string.IsNullOrEmpty(countryId)) {
				return;
			}

			if (_state.WorldCountries.DestroyedCountryIds.Contains(countryId)
				|| IsCountryQueuedForDestroy(countryId)) {
				_commands.Push(new SelectCountryCommand(""));
			}
		}

		bool IsCountryQueuedForDestroy(string countryId) {
			foreach (CountryDestroyedSnapshotState entry in _state.CountryDestroyedResults.Entries) {
				if (entry.CountryId == countryId) {
					return true;
				}
			}
			return false;
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new CountryDestroyedWindowView(_root, _loc);
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
