using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	public class OrgInfoDocument : MonoBehaviour {
		UIDocument _document;
		VisualState _state;
		ILocalization _loc;
		ResourceConfig _resourceConfig;
		CharacterConfig _characterConfig;
		CharacterVisualConfig _characterVisualConfig;
		TooltipSystem _tooltip;

		VisualElement _charsSlide;
		Button _charsToggleBtn;
		Label _orgName;
		ResourcesView _resourcesView;
		OrgCharactersView _charactersView;
		bool _charsOpen;

		VisualElement _actionsSlide;
		Button _actionsToggleBtn;
		OrgActionsView _actionsView;
		bool _actionsOpen;
		public event Action<bool> OnSubPanelOpened;
		ActionConfig _actionConfig;
		ActionVisualConfig _actionVisualConfig;
		CardPlayAnimator _cardPlayAnimator;

		[Inject]
		void Construct(VisualState state, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig,
			ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator) {
			_state = state;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
			_actionConfig = actionConfig;
			_actionVisualConfig = actionVisualConfig;
			_cardPlayAnimator = cardPlayAnimator;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var docRoot = _document.rootVisualElement;
			_tooltip = new TooltipSystem(docRoot);

			_orgName = docRoot.Q<Label>("org-name");
			_charsSlide = docRoot.Q("characters-slide");
			_charsToggleBtn = docRoot.Q<Button>("chars-toggle-btn");
			_actionsSlide = docRoot.Q("actions-slide");
			_actionsToggleBtn = docRoot.Q<Button>("actions-toggle-btn");

			if (_charsToggleBtn != null) {
				_charsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _charsToggleBtn.ContainsPoint(e.localPosition)) { ToggleChars(); }
				});
			}

			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _actionsToggleBtn.ContainsPoint(e.localPosition)) { ToggleActions(); }
				});
			}

			_document.rootVisualElement.style.display = DisplayStyle.None;
		}

		void Start() {
			InitViews();
		}

		void OnEnable() {
			if (_state == null) { return; }
			_state.PlayerOrganization.PropertyChanged  += HandleOrgChanged;
			_state.PlayerResources.PropertyChanged     += HandleResourcesChanged;
			_state.PlayerOrgCharacters.PropertyChanged += HandleCharactersChanged;
			_state.PlayerOrgActions.PropertyChanged    += HandleActionsChanged;
			Refresh();
		}

		void OnDisable() {
			if (_state == null) { return; }
			_state.PlayerOrganization.PropertyChanged  -= HandleOrgChanged;
			_state.PlayerResources.PropertyChanged     -= HandleResourcesChanged;
			_state.PlayerOrgCharacters.PropertyChanged -= HandleCharactersChanged;
			_state.PlayerOrgActions.PropertyChanged    -= HandleActionsChanged;
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
		}

		public void Show() {
			_document.rootVisualElement.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			_document.rootVisualElement.style.display = DisplayStyle.None;
			SetCharsOpen(false);
			SetActionsOpen(false);
		}

		public bool IsVisible => _document.rootVisualElement.style.display == DisplayStyle.Flex;

		void InitViews() {
			if (_resourcesView != null) { return; }
			if (_state == null || _loc == null) { return; }
			var docRoot = _document.rootVisualElement;
			_resourcesView = new ResourcesView(docRoot.Q("resources-container"), _loc, _resourceConfig, _tooltip);
			_charactersView = new OrgCharactersView(docRoot.Q("characters-container"), _loc, _characterConfig, _tooltip, _characterVisualConfig);
			var actionsInstance = docRoot.Q("org-actions-instance");
			if (actionsInstance != null) {
				_actionsView = new OrgActionsView(
					actionsInstance.Q("hand-container"),
					_loc, _actionConfig, _actionVisualConfig, _resourceConfig, _tooltip);
				_actionsView.OnCardClicked = OnActionCardClicked;
				_cardPlayAnimator?.SetActionsView(_actionsView);
			}
		}

		void Refresh() {
			if (_state == null) { return; }
			var org = _state.PlayerOrganization;
			if (!org.IsValid) { return; }
			if (_orgName != null) {
				_orgName.text = org.DisplayName;
			}
			_resourcesView?.Refresh(_state.PlayerResources);
			_charactersView?.Refresh(_state.PlayerOrgCharacters);
			_actionsView?.Refresh(_state.PlayerOrgActions, _state.PlayerResources);

			bool hasChars = _state.PlayerOrgCharacters.Slots.Count > 0;
			if (_charsToggleBtn != null) {
				_charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
			}

			bool hasActions = _state.PlayerOrgActions.Hand.Count > 0 || _state.PlayerOrgActions.Deck.Count > 0;
			if (_actionsToggleBtn != null) {
				_actionsToggleBtn.style.display = hasActions ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		void ToggleChars() {
			SetCharsOpen(!_charsOpen);
		}

		void SetCharsOpen(bool open) {
			if (open && _actionsOpen) { SetActionsOpen(false); }
			_charsOpen = open;
			if (_charsSlide != null) {
				if (open) {
					_charsSlide.AddToClassList("org-characters-slide--open");
				} else {
					_charsSlide.RemoveFromClassList("org-characters-slide--open");
				}
			}
			if (_charsToggleBtn != null) {
				var lbl = _charsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = open ? "Characters ▼" : "Characters ▲"; }
			}
			OnSubPanelOpened?.Invoke(_charsOpen || _actionsOpen);
		}

		void ToggleActions() {
			SetActionsOpen(!_actionsOpen);
		}

		void SetActionsOpen(bool open) {
			if (open && _charsOpen) { SetCharsOpen(false); }
			_actionsOpen = open;
			if (_actionsSlide != null) {
				if (open) {
					_actionsSlide.AddToClassList("org-actions-slide--open");
				} else {
					_actionsSlide.RemoveFromClassList("org-actions-slide--open");
				}
			}
			if (_actionsToggleBtn != null) {
				var lbl = _actionsToggleBtn.Q<Label>();
				if (lbl != null) { lbl.text = open ? "Actions ▼" : "Actions ▲"; }
			}
			OnSubPanelOpened?.Invoke(_charsOpen || _actionsOpen);
		}

		void OnActionCardClicked(string actionId, VisualElement cardElement) {
			if (_cardPlayAnimator == null || _state == null || !_state.PlayerOrganization.IsValid) { return; }
			_cardPlayAnimator.StartCardPlay(_state.PlayerOrganization.OrgId, actionId, cardElement);
		}

		void HandleOrgChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleResourcesChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleActionsChanged(object sender, PropertyChangedEventArgs e) => Refresh();
	}
}
