using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the ResourceChip atom against the resource icon tints SharedStyles.uss ships.</summary>
	public class ResourceChipGalleryBlock : GalleryBlockBase {
		static readonly List<string> _resources = new List<string> {
			"coin", "country-population", "country-recruits", "country-score", "org-score"
		};
		static readonly List<string> _states = new List<string> { "Value 42", "Value 1,234,567" };

		readonly ILocalization _loc;

		public override string Id => "resource-chip";
		public override string Title => "Atom: ResourceChip";
		protected override IReadOnlyList<string> InstanceChoices => _resources;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Resource";

		public ResourceChipGalleryBlock(ILocalization loc) {
			_loc = loc;
		}

		protected override void Render(VisualElement stage, string resourceId, int stateIndex) {
			ResourceChipBuilder.Elements chip = ResourceChipBuilder.Build();
			chip.Chip.AddToClassList("resource-row");
			string text = stateIndex == 1 ? "1,234,567" : "42";
			ResourceChipBuilder.Bind(chip, $"resource-icon--{resourceId}", text);
			chip.Label.AddToClassList("gs-label");
			stage.Add(chip.Chip);
		}
	}
}
