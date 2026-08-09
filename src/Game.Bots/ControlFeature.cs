using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public sealed class ControlFeature : IBotFeature {
		public const string Id = "control";
		public string FeatureId => Id;

		// Matches the control pool cap enforced in ControlSystem.ApplyChangeControl. Sourced from
		// GameSettings.MaxControlPool (game_settings.json) - the single place this value is configured -
		// rather than a per-feature parameter, since it is not bot-tunable behavior but a game rule.
		// The canonical playability result also checks this cap; retain the guard so this feature
		// does not scan cards for countries that cannot accept more control.
		readonly int _maxControlPool;

		public ControlFeature(IReadOnlyDictionary<string, double> parameters, int maxControlPool) {
			_ = parameters;
			_maxControlPool = maxControlPool;
		}

		public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) {
			if (TryPlayControl(obs, sink)) {
				return;
			}
			// Nothing playable raises control this tick. Rather than sit idle with a hand full
			// of cards that will never help, pay to discard one and roll a fresh draw offer -
			// see TryDiscardForBetterHand for the capacity/affordability guards.
			TryDiscardForBetterHand(obs, sink);
		}

		bool TryPlayControl(IBotObservation obs, IBotCommandSink sink) {
			foreach (var country in obs.Countries) {
				if (country.TotalControl >= _maxControlPool) { continue; }
				foreach (var card in country.Hand) {
					if (card.IsPlayable && card.RaisesControl) {
						sink.PlayCountryCard(card.ActionId, card.CountryId, card.SlotIndex, card.TargetCountryId);
						return true;
					}
				}
			}
			return false;
		}

		// Called only once TryPlayControl has already scanned every (card, acting-country)
		// combination and found nothing playable that raises control. The same physical card
		// shows up once per candidate acting country in obs.Countries[*].Hand (BotObservation
		// re-evaluates playability per context) - track the lowest SlotIndex seen instead of
		// the first entry so duplicates collapse onto one physical card deterministically.
		// Affordability (DiscardGoldCost) is not pre-checked here - DiscardCardSystem rejects
		// the command harmlessly if the org can't afford it, and ControlFeature naturally
		// retries once a day (via Bot's once-per-day gate) until gold from monthly income
		// covers the cost.
		bool TryDiscardForBetterHand(IBotObservation obs, IBotCommandSink sink) {
			if (obs.CountryHandCount < obs.CountryHandCapacity) {
				// Room already exists - the acquisition phase (Bot.TryAcquireCountryCard) draws
				// into it on its own; discarding here would waste a card for no reason.
				return false;
			}

			BotCardView? discardCandidate = null;
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					if (discardCandidate == null || card.SlotIndex < discardCandidate.SlotIndex) {
						discardCandidate = card;
					}
				}
			}
			if (discardCandidate == null) {
				return false;
			}

			sink.DiscardCountryCard(
				discardCandidate.ActionId, discardCandidate.CountryId, discardCandidate.SlotIndex, discardCandidate.TargetCountryId);
			return true;
		}
	}
}
