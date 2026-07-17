using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	// Composes the shared Score component onto Organization entities, mirroring
	// CountryScoreSystem (see ecs_patterns.md's Score composition pattern). Must run
	// after CountryScoreSystem.Update/Recompute in the same tick, since it reads the
	// already-computed Country + Score values.
	//
	// Recomputed daily rather than monthly (unlike CountryScoreSystem): control can
	// change from card plays at any time, and reacting to every individual trigger
	// (ChangeControlCommand, action effects, etc.) that could move it is unnecessary
	// complexity for a value that's otherwise cheap to recompute wholesale.
	public static class OrgScoreSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime) {
			bool isDayBoundary = previousTime.Date != currentTime.Date;
			if (!isDayBoundary) {
				return;
			}
			Recompute(world);
		}

		public static void Recompute(World world) {
			var scoreByCountryId = new Dictionary<string, double>();
			int[] countryScoreRequired = { TypeId<Country>.Value, TypeId<Score>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(countryScoreRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				Score[] scores = arch.GetColumn<Score>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					scoreByCountryId[countries[i].CountryId] = scores[i].Value;
				}
			}

			var controlByOrgId = new Dictionary<string, Dictionary<string, int>>();
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!controlByOrgId.TryGetValue(effects[i].OrgId, out var perCountry)) {
						perCountry = new Dictionary<string, int>();
						controlByOrgId[effects[i].OrgId] = perCountry;
					}
					perCountry.TryGetValue(effects[i].CountryId, out int existing);
					perCountry[effects[i].CountryId] = existing + effects[i].Value;
				}
			}

			// Collect (entity, orgId) pairs first — see CountryScoreSystem.Recompute's
			// comment for why world.Add cannot happen inline during archetype iteration.
			var orgEntities = new List<(int Entity, string OrgId)>();
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					orgEntities.Add((arch.Entities[i], orgs[i].OrganizationId));
				}
			}

			foreach (var (entity, orgId) in orgEntities) {
				double total = 0;
				if (controlByOrgId.TryGetValue(orgId, out var perCountry)) {
					foreach (var (countryId, control) in perCountry) {
						scoreByCountryId.TryGetValue(countryId, out double countryScore);
						total += (control / 100.0) * countryScore;
					}
				}

				if (world.Has<Score>(entity)) {
					world.Get<Score>(entity).Value = total;
				} else {
					world.Add(entity, new Score { Value = total });
				}
			}
		}

		public static double GetScore(IReadOnlyWorld world, string orgId) {
			int[] required = { TypeId<Organization>.Value, TypeId<Score>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				Score[] scores = arch.GetColumn<Score>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrganizationId == orgId) {
						return scores[i].Value;
					}
				}
			}
			return 0;
		}
	}
}
