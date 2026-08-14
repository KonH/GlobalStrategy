namespace GS.Game.Components {
	// Marker added to a country card deck the first time a draw offer is created for it.
	// DrawCardSystem checks its absence to guarantee a control-raising card in that one offer.
	[Savable]
	public struct FirstCardDrawCompleted { }
}
