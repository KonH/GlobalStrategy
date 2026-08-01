using GS.Game.Common;

namespace GS.Game.Components {
	// A country becomes eligible for revenge against a specific opponent when it loses a war
	// to that opponent, and stays eligible across repeat losses until it wins a war against
	// that same opponent (which clears the entry).
	[Savable]
	public struct RevengeEligibility {
		public string CountryId;
		public string TargetCountryId;
	}
}
