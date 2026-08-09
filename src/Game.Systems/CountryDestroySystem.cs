using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CountryDestroySystem {
		public static bool TryDestroyIfNoProvinces(World world, CountryRelations relations, string countryId) {
			if (string.IsNullOrEmpty(countryId)) {
				return false;
			}
			int countryEntity = FindCountryEntity(world, countryId);
			if (countryEntity < 0) {
				return false;
			}
			if (world.Has<IsDestroyed>(countryEntity)) {
				return false;
			}
			if (ProvinceOwnershipSystem.HasAnyProvince(world, countryId)) {
				return false;
			}

			world.Add(countryEntity, new IsDestroyed());
			if (world.Has<IsSelected>(countryEntity)) {
				world.Remove<IsSelected>(countryEntity);
			}
			ControlQuery.DestroyAllControlInCountry(world, countryId);
			relations.RemoveAllReferencing(world, countryId);

			int eventEntity = world.Create();
			world.Add(eventEntity, new CountryDestroyedApplied { CountryId = countryId });
			return true;
		}

		public static int DestroyAllZeroProvinceCountries(World world, CountryRelations relations) {
			var countryIds = new List<string>();
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					countryIds.Add(countries[i].CountryId);
				}
			}

			int newlyDestroyed = 0;
			foreach (string countryId in countryIds) {
				if (TryDestroyIfNoProvinces(world, relations, countryId)) {
					newlyDestroyed++;
				}
			}
			return newlyDestroyed;
		}

		public static bool IsCountryDestroyed(IReadOnlyWorld world, string countryId) {
			if (string.IsNullOrEmpty(countryId)) {
				return false;
			}
			int countryEntity = FindCountryEntity(world, countryId);
			return countryEntity >= 0 && world.Has<IsDestroyed>(countryEntity);
		}

		static int FindCountryEntity(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (countries[i].CountryId == countryId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
