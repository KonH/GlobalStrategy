using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ResourceEffect {
		[EffectId] public string EffectId;
		public double Value;
		public PayType PayType;
		public double AccumulatedTotal;
		public double MaxTotal;
		public bool ClampToZero;
		[OrgId] public string OrgId;
	}
}
