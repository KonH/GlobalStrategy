using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class OrgLensCountryView {
		readonly VisualElement _root;
		readonly Label _orgName;
		readonly Label _orgNoDominant;

		public OrgLensCountryView(VisualElement root) {
			_root = root;
			_orgName = root.Q<Label>("org-name");
			_orgNoDominant = root.Q<Label>("org-no-dominant");
			_root.style.display = DisplayStyle.None;
		}

		public void Refresh(SelectedCountryState country, OrgMapState orgMap, CountryInfluenceState influence) {
			if (!country.IsValid) {
				_root.style.display = DisplayStyle.None;
				return;
			}

			OrgCountryEntry found = null;
			foreach (var entry in orgMap.Entries) {
				if (entry.CountryId == country.CountryId) {
					found = entry;
					break;
				}
			}

			if (found != null) {
				string displayName = found.TopOrgId;
				foreach (var org in influence.OrgEntries) {
					if (org.OrgId == found.TopOrgId) {
						displayName = org.DisplayName;
						break;
					}
				}
				_orgName.text = displayName;
				_orgName.style.display = DisplayStyle.Flex;
				_orgNoDominant.style.display = DisplayStyle.None;
			} else {
				_orgName.style.display = DisplayStyle.None;
				_orgNoDominant.style.display = DisplayStyle.Flex;
			}

			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			_root.style.display = DisplayStyle.None;
		}
	}
}
