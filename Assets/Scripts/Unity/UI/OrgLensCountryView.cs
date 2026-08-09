#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class OrgLensCountryView {
		readonly VisualElement _root;
		readonly Label _orgName;
		readonly Label _orgNoDominant;
		readonly VisualElement? _flagElement;
		readonly ResourcesView _resourcesView;
		readonly OrgVisualConfig? _orgVisualConfig;

		public OrgLensCountryView(
			VisualElement root,
			ILocalization loc,
			ResourceConfig resourceConfig,
			TooltipSystem tooltip,
			OrgVisualConfig? orgVisualConfig = null) {
			_root = root;
			_orgName = root.Q<Label>("org-name");
			_orgNoDominant = root.Q<Label>("org-no-dominant");
			_flagElement = root.Q("org-flag");
			_resourcesView = new ResourcesView(root.Q("resources-container"), loc, resourceConfig, tooltip);
			_orgVisualConfig = orgVisualConfig;
			_root.style.display = DisplayStyle.None;
		}

		public void Refresh(
			SelectedCountryState country,
			OrgMapState orgMap,
			CountryControlState control,
			CountryResourcesState resources) {
			if (!country.IsValid) {
				_root.style.display = DisplayStyle.None;
				return;
			}

			OrgCountryEntry? found = null;
			foreach (var entry in orgMap.Entries) {
				if (entry.CountryId == country.CountryId) {
					found = entry;
					break;
				}
			}

			if (found != null) {
				string displayName = found.TopOrgId;
				foreach (var org in control.OrgEntries) {
					if (org.OrgId == found.TopOrgId) {
						displayName = org.DisplayName;
						break;
					}
				}
				_orgName.text = displayName;
				_orgName.style.display = DisplayStyle.Flex;
				_orgNoDominant.style.display = DisplayStyle.None;
				if (_flagElement != null) {
					var sprite = _orgVisualConfig?.Find(found.TopOrgId)?.flag;
					if (sprite != null) {
						_flagElement.style.backgroundImage = new StyleBackground(sprite);
						_flagElement.style.display = DisplayStyle.Flex;
					} else {
						_flagElement.style.display = DisplayStyle.None;
					}
				}
			} else {
				_orgName.style.display = DisplayStyle.None;
				_orgNoDominant.style.display = DisplayStyle.Flex;
				if (_flagElement != null) { _flagElement.style.display = DisplayStyle.None; }
			}

			_resourcesView.Refresh(resources);
			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			_root.style.display = DisplayStyle.None;
			if (_flagElement != null) { _flagElement.style.display = DisplayStyle.None; }
		}
	}
}
