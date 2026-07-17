using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public interface IBotObservation {
		string OrgId { get; }
		DateTime CurrentDate { get; }
		double Gold { get; }
		int OrgHandSize { get; }
		int TotalControl { get; }
		IReadOnlyList<BotCardView> OrgHand { get; }
		IReadOnlyList<string> DiscoveredCountryIds { get; }
		IReadOnlyList<BotCharacterSlotView> CharacterSlots { get; }
		IReadOnlyList<BotCountryView> Countries { get; }
		BotCountryView? GetCountry(string countryId);
	}
}
