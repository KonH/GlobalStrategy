using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the FlagNameHeader composite - a flag badge plus a name label - against every available country.</summary>
	public class FlagNameHeaderGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Default", "No flag" };

		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "flag-name-header";
		public override string Title => "Composite: FlagNameHeader";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Country";

		public FlagNameHeaderGalleryBlock(ILocalization loc, CountryVisualConfig countryVisualConfig, CountryConfig countryConfig) {
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
			FlagNameHeaderBuilder.Elements elements = FlagNameHeaderBuilder.Build();
			elements.Label.AddToClassList("tooltip-effect-name");
			Sprite sprite = stateIndex == 1 ? null : _countryVisualConfig?.Find(countryId)?.flag;
			FlagNameHeaderBuilder.Bind(elements, sprite, _loc.Get($"country_name.{countryId}"));
			stage.Add(elements.Row);
		}
	}
}
