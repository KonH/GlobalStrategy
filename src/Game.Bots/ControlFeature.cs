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

		public void CollectProposals(IBotObservation obs, IList<BotPlayProposal> proposals, Random rng) {
			_ = rng;
			foreach (var country in obs.Countries) {
				if (country.TotalControl >= _maxControlPool) {
					continue;
				}
				foreach (var card in country.Hand) {
					if (!card.IsPlayable || !card.RaisesControl) {
						continue;
					}
					proposals.Add(new BotPlayProposal {
						FeatureId = FeatureId,
						ActionId = card.ActionId,
						CountryId = card.CountryId,
						TargetCountryId = card.TargetCountryId,
						SlotIndex = card.SlotIndex,
						EstimatedDeltaOrgScore = EstimateDeltaOrgScore(country)
					});
				}
			}
		}

		double EstimateDeltaOrgScore(BotCountryView country) {
			// Child A CountryScore: org_score scales with control share of country_score.
			// Card control magnitude is not on the observation — use a 1-point proxy.
			if (country.CountryScore > 0) {
				return Math.Max(1.0, country.CountryScore / 100.0);
			}
			return Math.Max(1.0, _maxControlPool - country.MyControl);
		}
	}
}
