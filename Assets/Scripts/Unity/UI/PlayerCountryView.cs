using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class PlayerCountryView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly ILocalization _loc;

		public PlayerCountryView(VisualElement root, ILocalization loc) {
			_root = root;
			_name = root.Q<Label>("player-country-name");
			_loc = loc;
		}

		public void Refresh(PlayerCountryState state) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (state.IsValid) {
				_name.text = _loc.Get($"country_name.{state.CountryId}");
			}
		}
	}
}
