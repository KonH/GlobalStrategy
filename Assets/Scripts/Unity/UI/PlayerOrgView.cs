using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class PlayerOrgView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly ResourcesView _resourcesView;

		public PlayerOrgView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip) {
			_root = root;
			_name = root.Q<Label>("player-country-name");
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
		}

		public void Refresh(PlayerOrganizationState state, CountryResourcesState resources) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (state.IsValid) {
				_name.text = state.DisplayName;
			}
			_resourcesView.Refresh(resources);
		}
	}
}
