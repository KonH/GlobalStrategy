using System.ComponentModel;
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
		bool _ownsModalState;
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_state = state;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_root = _doc.rootVisualElement;
			_title = _root.Q<Label>("leaderboard-title");
			_closeButton = _root.Q<Button>("btn-close");
			_tabOrganizations = _root.Q<Button>("tab-organizations");
			_tabCountries = _root.Q<Button>("tab-countries");
			_empty = _root.Q<Label>("leaderboard-empty");
			_closeButton?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _closeButton.ContainsPoint(e.localPosition)) {
					Hide();
				}
			});
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

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		public void Show() {
			if (_root == null || _state == null) {
				return;
			}
			EnsureView();
			RefreshTexts();
			if (IsVisible) {
				_view?.Refresh(_state.Leaderboard);
				return;
			}
			ModalState.IsModalOpen = true;
			_ownsModalState = true;
			_view?.ResetToDefaultTab();
			_view?.Refresh(_state.Leaderboard);
			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
			if (_ownsModalState) {
				ModalState.IsModalOpen = false;
				_ownsModalState = false;
			}
		}

		void Subscribe() {
			if (_subscribed || _state == null) {
				return;
			}
			_state.Leaderboard.PropertyChanged += HandleLeaderboardChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.Leaderboard.PropertyChanged -= HandleLeaderboardChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_subscribed = false;
		}

		void HandleLeaderboardChanged(object sender, PropertyChangedEventArgs e) {
			if (!IsVisible) {
				return;
			}
			_view?.Refresh(_state.Leaderboard);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
			if (IsVisible) {
				_view?.Refresh(_state.Leaderboard);
			}
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
