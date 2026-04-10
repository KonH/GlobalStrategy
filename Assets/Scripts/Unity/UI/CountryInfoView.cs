using System;
using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class CountryInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly Button _selectButton;
		readonly ILocalization _loc;

		public Action OnSelectClicked;

		public CountryInfoView(VisualElement root, ILocalization loc) {
			_root = root;
			_name = root.Q<Label>("country-name");
			_selectButton = root.Q<Button>("select-player-button");
			_loc = loc;
			_selectButton.clicked += () => OnSelectClicked?.Invoke();
		}

		public void Refresh(SelectedCountryState selected, PlayerCountryState player) {
			_root.style.display = selected.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
			if (selected.IsValid) {
				_name.text = _loc.Get($"country_name.{selected.CountryId}");
				bool isPlayerCountry = player.IsValid && selected.CountryId == player.CountryId;
				_selectButton.style.display = isPlayerCountry ? DisplayStyle.None : DisplayStyle.Flex;
			}
		}
	}
}
