using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct Resource {
		[ResourceId] public string ResourceId;
		public double Value;
	}
}
