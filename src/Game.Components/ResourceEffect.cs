namespace GS.Game.Components {
	[Savable]
	public struct ResourceEffect {
		public string EffectId;
		public double Value;
		public PayType PayType;
		public double AccumulatedTotal;
		public double MaxTotal;
		public bool ClampToZero;
	}
}
