#nullable enable
using System;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;

namespace GS.Unity.UI {
	class ProvinceInfoView {
		readonly VisualElement _root;
		readonly Label _name;
		readonly VisualElement? _ownerRow;
		readonly VisualElement? _ownerFlag;
		readonly Label? _ownerName;
		readonly VisualElement? _occupantRow;
		readonly VisualElement? _occupantFlag;
		readonly Label? _occupantName;
		readonly ILocalization _loc;
		readonly ResourcesView _resourcesView;
		readonly CountryVisualConfig? _countryVisualConfig;
		string _ownerId = "";
		string _occupantId = "";

		public event Action<string>? OnCountryRowClicked;

		public ProvinceInfoView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig, TooltipSystem tooltip, CountryVisualConfig? countryVisualConfig) {
			_root = root;
			_name = root.Q<Label>("province-name");
			_ownerRow = root.Q("province-owner-row");
			_ownerFlag = root.Q("province-owner-flag");
			_ownerName = root.Q<Label>("province-owner-name");
			_occupantRow = root.Q("province-occupant-row");
			_occupantFlag = root.Q("province-occupant-flag");
			_occupantName = root.Q<Label>("province-occupant-name");
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_resourcesView = new ResourcesView(root.Q("province-resources-container"), loc, resourceConfig, tooltip);

			if (_ownerRow != null) {
				_ownerRow.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _ownerRow.ContainsPoint(e.localPosition) && !string.IsNullOrEmpty(_ownerId)) {
						OnCountryRowClicked?.Invoke(_ownerId);
					}
				});
			}
			if (_occupantRow != null) {
				_occupantRow.RegisterCallback<PointerUpEvent>(e => {
					if (e.button == 0 && _occupantRow.ContainsPoint(e.localPosition) && !string.IsNullOrEmpty(_occupantId)) {
						OnCountryRowClicked?.Invoke(_occupantId);
					}
				});
			}
		}

		public void Refresh(bool visible, string provinceId, string ownerId, string occupierId, CountryResourcesState resources) {
			_root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			if (!visible) {
				return;
			}

			_name.text = _loc.Get($"province_name.{provinceId}");

			_ownerId = ownerId;
			bool isOccupied = !string.IsNullOrEmpty(occupierId) && occupierId != ownerId;
			_occupantId = isOccupied ? occupierId : "";

			SetCountryChip(_ownerFlag, _ownerName, ownerId);
			_ownerRow?.EnableInClassList("province-owner-row--occupied", isOccupied);

			if (_occupantRow != null) {
				_occupantRow.style.display = isOccupied ? DisplayStyle.Flex : DisplayStyle.None;
				if (isOccupied) {
					SetCountryChip(_occupantFlag, _occupantName, occupierId);
				}
			}

			_resourcesView.Refresh(resources);
		}

		void SetCountryChip(VisualElement? flagEl, Label? nameLabel, string countryId) {
			if (nameLabel != null) {
				nameLabel.text = string.IsNullOrEmpty(countryId) ? "" : _loc.Get($"country_name.{countryId}");
			}
			if (flagEl == null) {
				return;
			}
			var sprite = _countryVisualConfig?.Find(countryId)?.flag;
			if (sprite != null) {
				flagEl.style.backgroundImage = new StyleBackground(sprite);
				flagEl.style.display = DisplayStyle.Flex;
			} else {
				flagEl.style.display = DisplayStyle.None;
			}
		}
	}
}
