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

		public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) {
			foreach (var card in obs.OrgHand) {
				if (TryPlay(obs, sink, card)) { return; }
			}
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					if (TryPlay(obs, sink, card)) { return; }
				}
			}
		}

		bool TryPlay(IBotObservation obs, IBotCommandSink sink, BotCardView card) {
			if (!card.IsPlayable || obs.Gold - card.GoldCost < _minGoldReserve) { return false; }
			if (card.CountryId == "") {
				sink.PlayOrgCard(card.ActionId);
			} else {
				sink.PlayCountryCard(card.ActionId, card.CountryId);
			}
			return true;
		}
	}
}
