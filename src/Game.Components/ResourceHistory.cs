using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ResourceHistory {
		[OmitFromSnapshot] public List<ResourceChangeEntry> History;
	}
}
