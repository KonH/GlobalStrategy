#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class PlayerOrgView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly VisualElement? _flagElement;
		readonly ResourcesView _resourcesView;
		readonly OrgVisualConfig? _orgVisualConfig;

		public PlayerOrgView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip, OrgVisualConfig? orgVisualConfig = null) {
			_root = root;
			_name = root.Q<Label>("player-country-name");
			_flagElement = root.Q("player-org-flag");
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_orgVisualConfig = orgVisualConfig;
		}

		public void Refresh(PlayerOrganizationState state, CountryResourcesState resources) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (state.IsValid) {
				_name.text = state.DisplayName;
				if (_flagElement != null) {
					var sprite = _orgVisualConfig?.Find(state.OrgId)?.flag;
					if (sprite != null) {
						_flagElement.style.backgroundImage = new StyleBackground(sprite);
						_flagElement.style.display = DisplayStyle.Flex;
					} else {
						_flagElement.style.display = DisplayStyle.None;
					}
				}
			}
			_resourcesView.Refresh(resources);
		}
	}
}
