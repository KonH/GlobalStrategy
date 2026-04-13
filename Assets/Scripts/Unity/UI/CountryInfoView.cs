using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;

		public CountryInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_loc = loc;
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
		}

		public void Refresh(SelectedCountryState selected, PlayerCountryState player, CountryResourcesState resources) {
			_root.style.display = selected.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (selected.IsValid) {
				_name.text = _loc.Get($"country_name.{selected.CountryId}");
			}
			_resourcesView.Refresh(resources);
		}
	}
}
