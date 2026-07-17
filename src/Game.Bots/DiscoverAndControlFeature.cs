using System;

namespace GS.Game.Bots {
	public sealed class DiscoverAndControlFeature : IBotFeature {
		public const string Id = "discoverAndControl";
		public string FeatureId => Id;

		public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) {
			foreach (var card in obs.OrgHand) {
				if (card.IsPlayable && card.DiscoversCountry) {
					sink.PlayOrgCard(card.ActionId);
					return;
				}
			}
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					if (card.IsPlayable && card.RaisesControl) {
						sink.PlayCountryCard(card.ActionId, card.CountryId);
						return;
					}
				}
			}
		}
	}
}
