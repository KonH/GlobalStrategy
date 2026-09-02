using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class OrgInfoDocument : MonoBehaviour {
		UIDocument _document;
		VisualState _state;
		ILocalization _loc;
		GameSettings _gameSettings;
		ResourceConfig _resourceConfig;
		CharacterConfig _characterConfig;
		CharacterVisualConfig _characterVisualConfig;
		OrgVisualConfig _orgVisualConfig;
		TooltipSystem _tooltip;

		OrgInfoView _view;
		public event Action<bool> OnSubPanelOpened;
		ActionConfig _actionConfig;
		ActionVisualConfig _actionVisualConfig;
		CardPlayAnimator _cardPlayAnimator;

		[Inject]
		void Construct(VisualState state, ILocalization loc, GameSettings gameSettings, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig,
			OrgVisualConfig orgVisualConfig, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig, CardPlayAnimator cardPlayAnimator) {
			_state = state;
			_loc = loc;
			_gameSettings = gameSettings;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_actionConfig = actionConfig;
			_actionVisualConfig = actionVisualConfig;
			_cardPlayAnimator = cardPlayAnimator;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var docRoot = _document.rootVisualElement;
			_tooltip = new TooltipSystem(docRoot);
			_document.rootVisualElement.style.display = DisplayStyle.None;
		}

		void Start() {
			InitView();
		}

		void OnEnable() {
			if (_state == null) { return; }
			_state.PlayerOrganization.PropertyChanged  += HandleOrgChanged;
			_state.PlayerOrganization.Resources.PropertyChanged     += HandleResourcesChanged;
			_state.PlayerOrganization.Characters.PropertyChanged += HandleCharactersChanged;
			_state.PlayerOrganization.Actions.PropertyChanged    += HandleActionsChanged;
			Refresh();
		}

		void OnDisable() {
			if (_state == null) { return; }
			_state.PlayerOrganization.PropertyChanged  -= HandleOrgChanged;
			_state.PlayerOrganization.Resources.PropertyChanged     -= HandleResourcesChanged;
			_state.PlayerOrganization.Characters.PropertyChanged -= HandleCharactersChanged;
			_state.PlayerOrganization.Actions.PropertyChanged    -= HandleActionsChanged;
		}

		void Update() {
			_tooltip?.Update(Time.deltaTime);
		}

		public void Show() {
			if (_state == null || _state.PlayerOrganization.IsDestroyed) {
				return;
			}

			_document.rootVisualElement.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			_document.rootVisualElement.style.display = DisplayStyle.None;
			_view?.SetCharsOpen(false);
			_view?.SetActionsOpen(false);
		}

		public bool IsVisible => _document.rootVisualElement.style.display == DisplayStyle.Flex;

		void InitView() {
			if (_view != null) { return; }
			if (_state == null || _loc == null) { return; }
			var docRoot = _document.rootVisualElement;
			_view = new OrgInfoView(
				docRoot, _loc, _resourceConfig, _characterConfig, _characterVisualConfig,
				_orgVisualConfig, _actionConfig, _actionVisualConfig, _tooltip);
			_view.OnSubPanelOpened += open => OnSubPanelOpened?.Invoke(open);
			if (_view.CharsToggleBtn != null) {
				_view.CharsToggleBtn.OnClick(_view.ToggleChars);
			}
			if (_view.ActionsToggleBtn != null) {
				_view.ActionsToggleBtn.OnClick(_view.ToggleActions);
			}
			if (_view.ActionsView != null) {
				_view.ActionsView.OnCardClicked = OnActionCardClicked;
				_cardPlayAnimator?.SetActionsView(_view.ActionsView);
			}
		}

		void Refresh() {
			if (_state == null) { return; }
			InitView();
			if (_view == null) { return; }
			var org = _state.PlayerOrganization;
			if (!org.IsValid) { return; }
			if (org.IsDestroyed) {
				Hide();
				return;
			}
			bool showControls = _gameSettings?.FeatureFlags?.ShowPlayerOrgControls ?? true;
			_view.Refresh(org, showControls);
		}

		void OnActionCardClicked(string actionId, int slotIndex, VisualElement cardElement) {
			if (_cardPlayAnimator == null || _state == null || !_state.PlayerOrganization.IsValid) { return; }
			_cardPlayAnimator.StartCardPlay(_state.PlayerOrganization.OrgId, actionId, slotIndex, cardElement);
		}

		void HandleOrgChanged(object sender, PropertyChangedEventArgs e) {
			if (_state != null && _state.PlayerOrganization.IsDestroyed) {
				Hide();
				return;
			}

			Refresh();
		}
		void HandleResourcesChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleActionsChanged(object sender, PropertyChangedEventArgs e) => Refresh();
	}
}
