using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public interface IBotObservation {
		string OrgId { get; }
		DateTime CurrentDate { get; }
		double Gold { get; }
		double OrgScore { get; }
		IReadOnlyList<BotOrgScoreView> OrgScores { get; }
		int OrgHandSize { get; }
		int CountryHandCount { get; }
		int CountryHandCapacity { get; }
		bool CanStartCountryCardDraw { get; }
		int TotalControl { get; }
		IReadOnlyList<BotCardView> OrgHand { get; }
		IReadOnlyList<BotCardDrawChoiceView> CountryCardDrawChoices { get; }
		IReadOnlyList<BotCharacterSlotView> CharacterSlots { get; }
		IReadOnlyList<BotCountryView> Countries { get; }
		BotCountryView? GetCountry(string countryId);
	}
}
