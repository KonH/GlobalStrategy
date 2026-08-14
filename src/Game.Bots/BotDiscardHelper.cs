using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public static class BotDiscardHelper {
		public static bool TryDiscardForBetterHand(
			IBotObservation obs,
			IBotCommandSink sink,
			Func<BotCardView, IBotObservation, double>? valueScore = null,
			double discardGoldCost = 0) {
			if (obs.CountryHandCount < obs.CountryHandCapacity) {
				return false;
			}
			if (discardGoldCost > 0 && obs.Gold < discardGoldCost) {
				return false;
			}

			Func<BotCardView, IBotObservation, double> scoreFn = valueScore ?? DefaultValueScore;
			BotCardView? discardCandidate = null;
			double discardValue = 0;
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					double value = scoreFn(card, obs);
					if (discardCandidate == null
						|| value < discardValue
						|| value == discardValue && card.SlotIndex < discardCandidate.SlotIndex) {
						discardCandidate = card;
						discardValue = value;
					}
				}
			}
			if (discardCandidate == null) {
				return false;
			}

			sink.DiscardCountryCard(
				discardCandidate.ActionId,
				discardCandidate.CountryId,
				discardCandidate.SlotIndex,
				discardCandidate.TargetCountryId);
			return true;
		}

		static double DefaultValueScore(BotCardView card, IBotObservation obs) {
			_ = obs;
			if (card.RaisesControl) {
				return 100;
			}
			if (BotDrawIntentScorer.IsKnownWarOrUnlockAction(card.ActionId)) {
				return 50;
			}
			if (card.IsPlayable) {
				return 10;
			}
			return 0;
		}
	}
}
