using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly ILocalization _loc;

		public CountryInfoView(VisualElement root, ILocalization loc) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_loc = loc;
		}

		public void Refresh(SelectedCountryState state) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (state.IsValid) {
				_name.text = _loc.Get($"country_name.{state.CountryId}");
			}
		}
	}
}
