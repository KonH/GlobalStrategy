using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class PlayerCountryView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;

		public PlayerCountryView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip) {
			_root = root;
			_name = root.Q<Label>("player-country-name");
			_loc = loc;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
		}

		public void Refresh(PlayerCountryState state, CountryResourcesState resources) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (state.IsValid) {
				_name.text = _loc.Get($"country_name.{state.CountryId}");
			}
			_resourcesView.Refresh(resources);
		}
	}
}
