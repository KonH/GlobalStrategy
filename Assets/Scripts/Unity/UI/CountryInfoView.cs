using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;

		public CountryInfoView(VisualElement root) {
			_root = root;
			_name = root.Q<Label>("country-name");
		}

		public void Refresh(SelectedCountryState state) {
			_root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			_name.text = state.CountryId ?? string.Empty;
		}
	}
}
