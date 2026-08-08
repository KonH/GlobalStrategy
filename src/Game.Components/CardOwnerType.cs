namespace GS.Game.Components {
	public enum CardOwnerKind {
		Org,
		Country
	}

	[Savable]
	public struct CardOwnerType {
		public CardOwnerKind Value;

		public CardOwnerType(CardOwnerKind value) {
			Value = value;
		}
	}
}
