using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the FlagBadge atom against every available country the CountryVisualConfig knows.</summary>
	public class FlagBadgeGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default", "No sprite" };

		readonly CountryVisualConfig _countryVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "flag-badge";
		public override string Title => "Atom: FlagBadge";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Country";

		public FlagBadgeGalleryBlock(CountryVisualConfig countryVisualConfig, CountryConfig countryConfig) {
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
			VisualElement flag = FlagBadgeBuilder.Build("flag-badge--large");
			Sprite sprite = stateIndex == 1 ? null : _countryVisualConfig?.Find(countryId)?.flag;
			FlagBadgeBuilder.Bind(flag, sprite);
			stage.Add(flag);
		}
	}
}
