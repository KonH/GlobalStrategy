using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CountryScoreSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime, double coefficient) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}
			Recompute(world, coefficient);
		}

		public static void Recompute(World world, double coefficient) {
			var provincesByOwner = ProvinceOwnershipSystem.GetProvincesByOwner(world);

			var populationByProvinceId = new Dictionary<string, double>();
			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerType != OwnerType.Province
						|| resources[i].ResourceId != ProvincePopulationGrowthSystem.PopulationResourceId) {
						continue;
					}
					populationByProvinceId[owners[i].OwnerId] = resources[i].Value;
				}
			}

			var scoreEntityByCountryId = new Dictionary<string, int>();
			int[] scoreRequired = { TypeId<CountryScore>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(scoreRequired, null)) {
				CountryScore[] scores = arch.GetColumn<CountryScore>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					scoreEntityByCountryId[scores[i].CountryId] = arch.Entities[i];
				}
			}

			// Collect country ids first — calling world.Create/Add inside GetMatchingArchetypes
			// would create new archetypes and mutate the dictionary mid-iteration, throwing
			// InvalidOperationException (same trap as InitSystem.DiscoverInitialCountries).
			var countryIds = new List<string>();
			int[] countryRequired = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(countryRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					countryIds.Add(countries[i].CountryId);
				}
			}

			foreach (string countryId in countryIds) {
				double totalPopulation = 0;
				if (provincesByOwner.TryGetValue(countryId, out var provinceIds)) {
					foreach (var provinceId in provinceIds) {
						if (populationByProvinceId.TryGetValue(provinceId, out double population)) {
							totalPopulation += population;
						}
					}
				}

				double value = coefficient * totalPopulation;
				if (scoreEntityByCountryId.TryGetValue(countryId, out int existingEntity)) {
					world.Get<CountryScore>(existingEntity).Value = value;
				} else {
					int scoreEntity = world.Create();
					world.Add(scoreEntity, new CountryScore { CountryId = countryId, Value = value });
				}
			}
		}

		public static double GetScore(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<CountryScore>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryScore[] scores = arch.GetColumn<CountryScore>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (scores[i].CountryId == countryId) {
						return scores[i].Value;
					}
				}
			}
			return 0;
		}
	}
}
