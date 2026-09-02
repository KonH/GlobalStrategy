using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the StatChip atom used by character skill rows.</summary>
	public class StatChipGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Skill" };
		static readonly List<string> _states = new List<string> { "Low (1)", "Mid (5)", "High (9)" };

		readonly ILocalization _loc;

		public override string Id => "stat-chip";
		public override string Title => "Atom: StatChip";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public StatChipGalleryBlock(ILocalization loc) {
			_loc = loc;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			StatChipBuilder.Elements chip = StatChipBuilder.Build();
			int value = stateIndex switch { 0 => 1, 2 => 9, _ => 5 };
			StatChipBuilder.Bind(chip, value.ToString());
			stage.Add(chip.Chip);
		}
	}
}
