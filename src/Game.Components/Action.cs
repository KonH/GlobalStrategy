namespace GS.Game.Components {
	// Savable: persistent card identity, present on the card entity from creation.
	// CardUse marks which entity is being processed by the pipeline in the current frame.
	[Savable]
	public struct GameAction {
		public string ActionId;
	}
}
