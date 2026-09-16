using GS.Game.Common;

namespace GS.Game.Components {
	public struct ResourceChange {
		[EffectId] public string EffectId;
		[ResourceId] public string ResourceId;
		[OwnerId(OwnerTypeSibling = null)] public string OwnerId;
		public double Amount;
	}
}
