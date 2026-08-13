using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public sealed class BaselineCardPlayFeature : IBotFeature {
		public const string Id = "baselineCardPlay";

		readonly double _minGoldReserve;

		public BaselineCardPlayFeature(IReadOnlyDictionary<string, double> parameters) {
			_minGoldReserve = parameters.TryGetValue("minGoldReserve", out var v) ? v : 0.0;
		}

		public string FeatureId => Id;

		public void CollectProposals(IBotObservation obs, IList<BotPlayProposal> proposals, Random rng) {
			_ = rng;
			foreach (var card in obs.OrgHand) {
				if (TryPropose(obs, proposals, card)) {
					return;
				}
			}
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					if (TryPropose(obs, proposals, card)) {
						return;
					}
				}
			}
		}

		bool TryPropose(IBotObservation obs, IList<BotPlayProposal> proposals, BotCardView card) {
			if (!card.IsPlayable || obs.Gold - card.GoldCost < _minGoldReserve) {
				return false;
			}
			proposals.Add(new BotPlayProposal {
				FeatureId = FeatureId,
				ActionId = card.ActionId,
				CountryId = card.CountryId,
				TargetCountryId = card.TargetCountryId,
				SlotIndex = card.SlotIndex,
				EstimatedDeltaOrgScore = 1.0
			});
			return true;
		}
	}
}
