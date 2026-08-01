using GS.Game.Common;

namespace GS.Game.Components {
	// Identifies the country a revenge card can be used against. The card's
	// CountryContext is the country seeking revenge.
	[Savable]
	public struct RevengeCardTarget {
		public string TargetCountryId;
	}
}
