using System.ComponentModel;
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
		ILocalization _loc;
		OrgVisualConfig _orgVisualConfig;
		UIDocument _doc;
		VisualElement _root;
		Label _title;
		Button _closeButton;
		GoalsWindowView _view;
		bool _ownsModalState;
		bool _subscribed;

		[Inject]
		void Construct(VisualState state, ILocalization loc, OrgVisualConfig orgVisualConfig) {
			_state = state;
			_loc = loc;
			_orgVisualConfig = orgVisualConfig;
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
			_view?.ResetToPlayerOrg(_state.PlayerOrganization.OrgId);
			_view?.Refresh(_state.Leaderboard, _state.Goals);
			if (IsVisible) {
				return;
			}
			ModalState.IsModalOpen = true;
			_ownsModalState = true;
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
			_state.Goals.PropertyChanged += HandleGoalsChanged;
			_state.Locale.PropertyChanged += HandleLocaleChanged;
			_state.PlayerOrganization.PropertyChanged += HandlePlayerOrganizationChanged;
			_subscribed = true;
		}

		void Unsubscribe() {
			if (!_subscribed || _state == null) {
				return;
			}
			_state.Leaderboard.PropertyChanged -= HandleLeaderboardChanged;
			_state.Goals.PropertyChanged -= HandleGoalsChanged;
			_state.Locale.PropertyChanged -= HandleLocaleChanged;
			_state.PlayerOrganization.PropertyChanged -= HandlePlayerOrganizationChanged;
			_subscribed = false;
		}

		void HandleLeaderboardChanged(object sender, PropertyChangedEventArgs e) {
			if (!IsVisible) {
				return;
			}
			_view?.Refresh(_state.Leaderboard, _state.Goals);
		}

		void HandleGoalsChanged(object sender, PropertyChangedEventArgs e) {
			if (!IsVisible) {
				return;
			}
			_view?.Refresh(_state.Leaderboard, _state.Goals);
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			// HUD owns locale switching — only refresh bound texts/view here.
			RefreshTexts();
			if (IsVisible) {
				_view?.Refresh(_state.Leaderboard, _state.Goals);
			}
		}

		void HandlePlayerOrganizationChanged(object sender, PropertyChangedEventArgs e) {
			if (!IsVisible) {
				return;
			}
			_view?.Refresh(_state.Leaderboard, _state.Goals);
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
