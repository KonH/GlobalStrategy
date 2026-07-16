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

			// Collect (entity, countryId) pairs first — calling world.Add inside
			// GetMatchingArchetypes would create new archetypes and mutate the
			// dictionary mid-iteration, throwing InvalidOperationException (same
			// trap as InitSystem.DiscoverInitialCountries).
			var countryEntities = new List<(int Entity, string CountryId)>();
			int[] countryRequired = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(countryRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					countryEntities.Add((arch.Entities[i], countries[i].CountryId));
				}
			}

			// Second pass — compute + attach/update Score directly on the Country
			// entity, no separate CountryId -> scoreEntity lookup needed (see ecs_patterns.md).
			foreach (var (entity, countryId) in countryEntities) {
				double totalPopulation = 0;
				if (provincesByOwner.TryGetValue(countryId, out var provinceIds)) {
					foreach (var provinceId in provinceIds) {
						if (populationByProvinceId.TryGetValue(provinceId, out double population)) {
							totalPopulation += population;
						}
					}
				}

				double value = coefficient * totalPopulation;
				if (world.Has<Score>(entity)) {
					world.Get<Score>(entity).Value = value;
				} else {
					world.Add(entity, new Score { Value = value });
				}
			}
		}

		public static double GetScore(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<Country>.Value, TypeId<Score>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				Score[] scores = arch.GetColumn<Score>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (countries[i].CountryId == countryId) {
						return scores[i].Value;
					}
				}
			}
			return 0;
		}
	}
}
