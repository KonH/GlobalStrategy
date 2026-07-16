using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	// Derived query only — no ECS component, nothing [Savable], no VisualState exposure.
	// Reads already-computed CountryScore values (see CountryScoreSystem, spec 47) plus
	// the org's ControlEffects, and is the ONLY place in the codebase that knows the
	// org-score formula. Consumers treat the result as an opaque comparable number.
	public static class OrgScore {
		public static double GetScore(IReadOnlyWorld world, string orgId) {
			// Read every country's score in one pass — avoids calling the linear-scan
			// CountryScoreSystem.GetScore once per country the org holds control in,
			// which would be O(countries) per lookup, O(countries^2) overall.
			var scoreByCountryId = new Dictionary<string, double>();
			int[] scoreRequired = { TypeId<Country>.Value, TypeId<Score>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(scoreRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				Score[] scores = arch.GetColumn<Score>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					scoreByCountryId[countries[i].CountryId] = scores[i].Value;
				}
			}

			// Sum this org's ControlEffect.Value per country in one pass.
			var controlByCountryId = new Dictionary<string, int>();
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId != orgId) {
						continue;
					}
					if (!controlByCountryId.TryGetValue(effects[i].CountryId, out int existing)) {
						existing = 0;
					}
					controlByCountryId[effects[i].CountryId] = existing + effects[i].Value;
				}
			}

			double total = 0;
			foreach (var (countryId, control) in controlByCountryId) {
				scoreByCountryId.TryGetValue(countryId, out double countryScore);
				total += (control / 100.0) * countryScore;
			}
			return total;
		}
	}
}
