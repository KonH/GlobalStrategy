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

		[Inject]
		void Construct(VisualState state, ILocalization loc, ResourceConfig resourceConfig, CharacterConfig characterConfig, CharacterVisualConfig characterVisualConfig) {
			_state = state;
			_loc = loc;
			_resourceConfig = resourceConfig;
			_characterConfig = characterConfig;
			_characterVisualConfig = characterVisualConfig;
		}

		void Awake() {
			_document = GetComponent<UIDocument>();
			var docRoot = _document.rootVisualElement;
			_tooltip = new TooltipSystem(docRoot);

			_orgName = docRoot.Q<Label>("org-name");
			_charsSlide = docRoot.Q("characters-slide");
			_charsToggleBtn = docRoot.Q<Button>("chars-toggle-btn");

			if (_charsToggleBtn != null) {
				_charsToggleBtn.clicked += ToggleChars;
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
			Refresh();
		}

		void OnDisable() {
			if (_state == null) { return; }
			_state.PlayerOrganization.PropertyChanged  -= HandleOrgChanged;
			_state.PlayerResources.PropertyChanged     -= HandleResourcesChanged;
			_state.PlayerOrgCharacters.PropertyChanged -= HandleCharactersChanged;
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
		}

		public bool IsVisible => _document.rootVisualElement.style.display == DisplayStyle.Flex;

		void InitViews() {
			if (_resourcesView != null) { return; }
			if (_state == null || _loc == null) { return; }
			var docRoot = _document.rootVisualElement;
			_resourcesView = new ResourcesView(docRoot.Q("resources-container"), _loc, _resourceConfig, _tooltip);
			_charactersView = new OrgCharactersView(docRoot.Q("characters-container"), _loc, _characterConfig, _tooltip, _characterVisualConfig);
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

			bool hasChars = _state.PlayerOrgCharacters.Slots.Count > 0;
			if (_charsToggleBtn != null) {
				_charsToggleBtn.style.display = hasChars ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		void ToggleChars() {
			SetCharsOpen(!_charsOpen);
		}

		void SetCharsOpen(bool open) {
			_charsOpen = open;
			if (_charsSlide != null) {
				if (open) {
					_charsSlide.AddToClassList("org-characters-slide--open");
				} else {
					_charsSlide.RemoveFromClassList("org-characters-slide--open");
				}
			}
			if (_charsToggleBtn != null) {
				_charsToggleBtn.text = open ? "▼ Characters" : "▲ Characters";
			}
		}

		void HandleOrgChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleResourcesChanged(object sender, PropertyChangedEventArgs e) => Refresh();
		void HandleCharactersChanged(object sender, PropertyChangedEventArgs e) => Refresh();
	}
}
