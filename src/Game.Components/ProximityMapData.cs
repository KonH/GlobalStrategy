using System.Collections.Generic;

using GS.Game.Common;

namespace GS.Game.Components {
	public struct ProximityMapData {
		[OmitFromSnapshot] public Dictionary<(string, string), float> Distances;
	}
}
