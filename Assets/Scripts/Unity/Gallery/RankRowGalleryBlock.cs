using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the RankRow component used by Leaderboard/Goals/EndGame lists.</summary>
	public class RankRowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Normal", "Highlighted" };

		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly List<string> _countryIds = new();

		public override string Id => "rank-row";
		public override string Title => "Row: RankRow";
		protected override IReadOnlyList<string> InstanceChoices => _countryIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Country";

		public RankRowGalleryBlock(ILocalization loc, CountryVisualConfig countryVisualConfig, CountryConfig countryConfig) {
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
			Sprite sprite = _countryVisualConfig?.Find(countryId)?.flag;
			string name = _loc.Get($"country_name.{countryId}");
			RankRowBuilder.Elements elements = RankRowBuilder.Build();
			RankRowBuilder.Bind(elements, 1, sprite, name, "1,234", highlighted: stateIndex == 1);
			stage.Add(elements.Row);
		}
	}
}
