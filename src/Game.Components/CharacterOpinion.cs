using System.Collections.Generic;

namespace GS.Game.Components {
	[Savable]
	public struct CharacterOpinion {
		public Dictionary<string, int> BaseOpinionPerOrg;
		public Dictionary<string, List<OpinionModifier>> ModifiersPerOrg;
	}
}
