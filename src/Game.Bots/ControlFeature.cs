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
			TryPlayControl(obs, sink);
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
	}
}
