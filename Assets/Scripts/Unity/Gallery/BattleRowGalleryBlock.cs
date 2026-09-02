using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the BattleRow component used by WarProgress/WarResult battle lists.</summary>
	public class BattleRowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Battle" };
		static readonly List<string> _states = new List<string> { "In progress", "Finished" };

		readonly ILocalization _loc;

		public override string Id => "battle-row";
		public override string Title => "Row: BattleRow";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public BattleRowGalleryBlock(ILocalization loc) {
			_loc = loc;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			var label = BattleRowBuilder.Build();
			label.text = stateIndex == 1
				? "Battle at Normandy (France won, -120 / -80)"
				: "Battle at Normandy (progress +5, 400 vs 350 troops)";
			stage.Add(label);
		}
	}
}
