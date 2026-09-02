using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the RequirementRow component used by action cards and the debug panel.</summary>
	public class RequirementRowGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Requirement" };
		static readonly List<string> _states = new List<string> { "Passed", "Failed", "Muted" };

		public override string Id => "requirement-row";
		public override string Title => "Row: RequirementRow";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			var label = RequirementRowBuilder.Build();
			switch (stateIndex) {
				case 0:
					RequirementRowBuilder.Bind(label, "Control >= 25%", true);
					break;
				case 1:
					RequirementRowBuilder.Bind(label, "Control >= 25%", false);
					break;
				default:
					RequirementRowBuilder.BindMuted(label, "Selected country: France");
					break;
			}
			stage.Add(label);
		}
	}
}
