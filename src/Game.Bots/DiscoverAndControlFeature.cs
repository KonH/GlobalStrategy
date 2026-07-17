using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public sealed class DiscoverAndControlFeature : IBotFeature {
		public const string Id = "discoverAndControl";
		public string FeatureId => Id;

		// double.MaxValue means "never met" - preserves the original discover-first,
		// control-fallback ordering when no threshold parameter is supplied.
		readonly double _discoveredCountriesAvailableControl;

		// Matches the control pool cap enforced in ControlSystem.ApplyChangeControl. Sourced from
		// GameSettings.MaxControlPool (game_settings.json) - the single place this value is configured -
		// rather than a per-feature parameter, since it is not bot-tunable behavior but a game rule.
		// IsPlayable does not check remaining pool room, so a full country would otherwise still look playable.
		readonly int _maxControlPool;

		public DiscoverAndControlFeature(IReadOnlyDictionary<string, double> parameters, int maxControlPool) {
			_discoveredCountriesAvailableControl = parameters.TryGetValue("discoveredCountriesAvailableControl", out var v) ? v : double.MaxValue;
			_maxControlPool = maxControlPool;
		}

		public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) {
			bool preferControl = obs.DiscoveredCountryIds.Count >= _discoveredCountriesAvailableControl;

			if (preferControl) {
				if (TryPlayControl(obs, sink)) { return; }
				if (TryPlayDiscover(obs, sink)) { return; }
			} else {
				if (TryPlayDiscover(obs, sink)) { return; }
				if (TryPlayControl(obs, sink)) { return; }
			}
		}

		static bool TryPlayDiscover(IBotObservation obs, IBotCommandSink sink) {
			foreach (var card in obs.OrgHand) {
				if (card.IsPlayable && card.DiscoversCountry) {
					sink.PlayOrgCard(card.ActionId);
					return true;
				}
			}
			return false;
		}

		bool TryPlayControl(IBotObservation obs, IBotCommandSink sink) {
			foreach (var country in obs.Countries) {
				if (country.TotalControl >= _maxControlPool) { continue; }
				foreach (var card in country.Hand) {
					if (card.IsPlayable && card.RaisesControl) {
						sink.PlayCountryCard(card.ActionId, card.CountryId);
						return true;
					}
				}
			}
			return false;
		}
	}
}
