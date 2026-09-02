using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the ProvinceTransferRow component used by WarResultWindow.</summary>
	public class ProvinceTransferRowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Transfer" };

		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "province-transfer-row";
		public override string Title => "Row: ProvinceTransferRow";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "New owner";

		public ProvinceTransferRowGalleryBlock(ILocalization loc, CountryVisualConfig countryVisualConfig, CountryConfig countryConfig) {
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			if (_countryVisualConfig != null) {
				foreach (CountryVisualEntry entry in _countryVisualConfig.Entries) {
					if (!HudConfigLoader.IsCountryAvailable(countryConfig, entry.countryId)) {
						continue;
					}
					_countryIds.Add(entry.countryId);
				}
			}
		}

		protected override void Render(VisualElement stage, string countryId, int stateIndex) {
			Sprite oldFlag = _countryIds.Count > 0 ? _countryVisualConfig?.Find(_countryIds[0])?.flag : null;
			Sprite newFlag = _countryVisualConfig?.Find(countryId)?.flag;
			ProvinceTransferRowBuilder.Elements elements = ProvinceTransferRowBuilder.Build();
			ProvinceTransferRowBuilder.Bind(elements, oldFlag, newFlag, "Sample Province");
			stage.Add(elements.Row);
		}
	}
}
