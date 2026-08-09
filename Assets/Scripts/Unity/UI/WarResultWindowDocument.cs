using System.ComponentModel;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class WarResultWindowDocument : MonoBehaviour {
		VisualState _state;
		ILocalization _loc;
		CountryVisualConfig _countryVisualConfig;
		EffectConfig _effectConfig;
		IWriteOnlyCommandAccessor _commands;
		UIDocument _doc;
		VisualElement _root;
		WarResultWindowView _view;
		TooltipSystem _tooltip;
		ModalState _modalState;
		bool _issuedPause;
		bool _subscribed;
		bool _timeSubscribed;

		[Inject]
		void Construct(
			VisualState state,
			ILocalization loc,
			CountryVisualConfig countryVisualConfig,
			EffectConfig effectConfig,
			IWriteOnlyCommandAccessor commands,
			ModalState modalState) {
			_state = state;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_effectConfig = effectConfig;
			_commands = commands;
			_modalState = modalState;
		}

		const int SortingOrder = 510;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
			_root = _doc.rootVisualElement;
			_tooltip = new TooltipSystem(_root);
			_modalState.Unlocked += HandleModalUnlocked;
			Button closeButton = _root.Q<Button>("btn-close");
			closeButton?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && closeButton.ContainsPoint(e.localPosition)) {
					Hide();
				}
			});
			HideVisualOnly();
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
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
			if (!_state.WarResults.TryPeek(out WarResultSnapshotState snapshot) || snapshot == null) {
				return;
			}

			EnsureView();
			if (!snapshot.ShouldPause && _issuedPause) {
				_commands?.Push(new UnpauseCommand());
				_issuedPause = false;
			} else if (snapshot.ShouldPause && !_state.Time.IsPaused) {
				_commands?.Push(new PauseCommand());
				_issuedPause = true;
			}

			_modalState.Lock(this);
			_root.style.display = DisplayStyle.Flex;
			RefreshTexts();
			_view?.Refresh(snapshot);
			UpdateTimeSubscription();
		}

		public void Hide() {
			HideVisualOnly();
			// Unlock fires ModalState.Unlocked → both this and any other modal window (e.g.
			// CountryDestroyedWindowDocument) re-check whether they can open now.
			_modalState.Unlock(this);

			// AcknowledgeCurrent raises PropertyChanged → TryOpenIfQueued → OpenCurrent for the
			// next FIFO item; do not call OpenCurrent again here (duplicate PauseCommand / bind).
			_state?.WarResults.AcknowledgeCurrent();

			if (_state == null || !_state.WarResults.TryPeek(out _)) {
				if (_issuedPause) {
					_commands?.Push(new UnpauseCommand());
					_issuedPause = false;
				}
			}
			UpdateTimeSubscription();
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
			_state.WarResults.PropertyChanged += HandleWarResultsChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
			UpdateTimeSubscription();
		}

		void Unsubscribe() {
			if (_subscribed && _state != null) {
				_state.WarResults.PropertyChanged -= HandleWarResultsChanged;
				_state.Locale.PropertyChanged -= HandleLocaleChanged;
				_subscribed = false;
			}
			UnsubscribeTime();
		}

		void UpdateTimeSubscription() {
			if (_state == null) {
				return;
			}
			bool needsDefense = NeedsPauseDefense();
			if (needsDefense && !_timeSubscribed) {
				_state.Time.PropertyChanged += HandleTimeChanged;
				_timeSubscribed = true;
			} else if (!needsDefense && _timeSubscribed) {
				UnsubscribeTime();
			}
		}

		void UnsubscribeTime() {
			if (!_timeSubscribed || _state == null) {
				return;
			}
			_state.Time.PropertyChanged -= HandleTimeChanged;
			_timeSubscribed = false;
		}

		bool NeedsPauseDefense() {
			if (_state == null) {
				return false;
			}
			foreach (WarResultSnapshotState entry in _state.WarResults.Entries) {
				if (entry.ShouldPause) {
					return true;
				}
			}
			return false;
		}

		void HandleWarResultsChanged(object sender, PropertyChangedEventArgs e) {
			TryOpenIfQueued();
			UpdateTimeSubscription();
		}

		void TryOpenIfQueued() {
			if (IsVisible) {
				return;
			}
			if (_state == null || !_state.WarResults.TryPeek(out _)) {
				return;
			}
			// Any other modal window (e.g. CountryDestroyedWindow) currently holding the lock keeps
			// this one queued until it closes — mutual exclusion between modal windows by default.
			if (_modalState.IsLocked()) {
				return;
			}
			OpenCurrent();
		}

		void HandleModalUnlocked() {
			TryOpenIfQueued();
		}

		void HandleTimeChanged(object sender, PropertyChangedEventArgs e) {
			if (_state == null || _state.Time.IsPaused) {
				return;
			}
			if (!NeedsPauseDefense()) {
				return;
			}
			_commands?.Push(new PauseCommand());
			_issuedPause = true;
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
			if (IsVisible && _state != null && _state.WarResults.TryPeek(out WarResultSnapshotState snapshot)) {
				_view?.Refresh(snapshot);
			}
		}

		void EnsureView() {
			if (_view != null || _root == null) {
				return;
			}
			_view = new WarResultWindowView(_root, _loc, _countryVisualConfig, _effectConfig, _tooltip);
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
