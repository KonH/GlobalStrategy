using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the ResourceChip atom against the resource icon tints SharedStyles.uss ships.</summary>
	public class ResourceChipGalleryBlock : GalleryBlockBase {
		// Fail-open fallback matching HudConfigLoader.LoadResourceConfig returning null for an
		// unwired TextAsset (see HudPanelGalleryBlocks.cs IsCountryAvailable precedent) - without
		// it an unwired asset would silently empty this block's instance dropdown.
		static readonly List<string> _fallbackResources = new List<string> {
			"gold", "country_population", "recruits", "country_score", "org_score", "population"
		};
		static readonly List<string> _states = new List<string> { "Value 42", "Value 1,234,567" };

		readonly ILocalization _loc;
		readonly List<string> _resources;

		public override string Id => "resource-chip";
		public override string Title => "Atom: ResourceChip";
		protected override IReadOnlyList<string> InstanceChoices => _resources;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Resource";

		public ResourceChipGalleryBlock(ILocalization loc, TextAsset resourceConfigAsset) {
			_loc = loc;
			ResourceConfig resourceConfig = HudConfigLoader.LoadResourceConfig(resourceConfigAsset);
			_resources = resourceConfig != null
				? new List<string>(resourceConfig.DisplayWhitelist)
				: new List<string>(_fallbackResources);
		}

		protected override void Render(VisualElement stage, string resourceId, int stateIndex) {
			ResourceChipBuilder.Elements chip = ResourceChipBuilder.Build();
			chip.Chip.AddToClassList("resource-row");
			string text = stateIndex == 1 ? "1,234,567" : "42";
			ResourceChipBuilder.Bind(chip, ResourceDisplayNaming.IconClass(resourceId), text);
			chip.Label.AddToClassList("gs-label");
			stage.Add(chip.Chip);
		}
	}
}
