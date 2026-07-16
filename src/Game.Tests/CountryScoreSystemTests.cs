using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryScoreSystemTests {
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1);
		static readonly DateTime Jan15 = new DateTime(1880, 1, 15);
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1);
		static readonly DateTime Mar20 = new DateTime(1880, 3, 20);

		static int SeedCountry(World world, string countryId) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			return entity;
		}

		static void SeedProvince(World world, string provinceId, string ownerId, double population) {
			ProvinceOwnershipSystem.Seed(world, new GS.Game.Configs.ProvinceConfig {
				Provinces = new System.Collections.Generic.List<GS.Game.Configs.ProvinceEntry> {
					new GS.Game.Configs.ProvinceEntry { ProvinceId = provinceId, CountryId = ownerId, Population = population }
				}
			});
			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(provinceId, OwnerType.Province));
			world.Add(resourceEntity, new Resource {
				ResourceId = ProvincePopulationGrowthSystem.PopulationResourceId,
				Value = population
			});
		}

		[Fact]
		void score_computed_from_owned_province_population_at_month_boundary() {
			var world = new World();
			SeedCountry(world, "A");
			SeedProvince(world, "prov_1", "A", 1000);
			SeedProvince(world, "prov_2", "A", 2000);

			CountryScoreSystem.Update(world, Jan31, Feb1, 0.01);

			Assert.Equal(30.0, CountryScoreSystem.GetScore(world, "A"));
		}

		[Fact]
		void country_with_zero_owned_provinces_has_zero_score() {
			var world = new World();
			SeedCountry(world, "A");

			CountryScoreSystem.Recompute(world, 0.01);

			Assert.Equal(0.0, CountryScoreSystem.GetScore(world, "A"));
		}

		[Fact]
		void score_unchanged_within_same_month() {
			var world = new World();
			SeedCountry(world, "A");
			SeedProvince(world, "prov_1", "A", 1000);

			CountryScoreSystem.Recompute(world, 0.01);
			double before = CountryScoreSystem.GetScore(world, "A");

			CountryScoreSystem.Update(world, Jan1, Jan15, 0.01);

			Assert.Equal(before, CountryScoreSystem.GetScore(world, "A"));
		}

		[Fact]
		void ownership_change_mid_month_does_not_affect_score_until_boundary() {
			var world = new World();
			SeedCountry(world, "A");
			SeedCountry(world, "B");
			SeedProvince(world, "prov_1", "A", 1000);

			CountryScoreSystem.Recompute(world, 0.01);
			double scoreABefore = CountryScoreSystem.GetScore(world, "A");
			double scoreBBefore = CountryScoreSystem.GetScore(world, "B");

			ProvinceOwnershipSystem.ChangeOwner(world, "prov_1", "B");
			CountryScoreSystem.Update(world, Jan1, Jan15, 0.01);

			Assert.Equal(scoreABefore, CountryScoreSystem.GetScore(world, "A"));
			Assert.Equal(scoreBBefore, CountryScoreSystem.GetScore(world, "B"));
		}

		[Fact]
		void multiple_months_skipped_recomputes_once_from_current_state() {
			var world = new World();
			SeedCountry(world, "A");
			SeedProvince(world, "prov_1", "A", 1000);

			CountryScoreSystem.Update(world, Jan15, Mar20, 0.01);

			Assert.Equal(10.0, CountryScoreSystem.GetScore(world, "A"));
		}

		[Fact]
		void recompute_reads_current_runtime_owner_not_seed_country_id() {
			var world = new World();
			SeedCountry(world, "A");
			SeedCountry(world, "B");
			SeedProvince(world, "prov_1", "A", 1000);

			ProvinceOwnershipSystem.ChangeOwner(world, "prov_1", "B");
			CountryScoreSystem.Recompute(world, 0.01);

			Assert.Equal(0.0, CountryScoreSystem.GetScore(world, "A"));
			Assert.Equal(10.0, CountryScoreSystem.GetScore(world, "B"));
		}

		[Fact]
		void recompute_is_forced_and_ungated() {
			var world = new World();
			SeedCountry(world, "A");
			SeedProvince(world, "prov_1", "A", 1000);

			CountryScoreSystem.Recompute(world, 0.01);

			Assert.Equal(10.0, CountryScoreSystem.GetScore(world, "A"));
		}

		[Fact]
		void get_score_returns_zero_for_unknown_country() {
			var world = new World();

			Assert.Equal(0.0, CountryScoreSystem.GetScore(world, "Unknown"));
		}

		[Fact]
		void score_is_composed_onto_the_country_entity_not_a_separate_entity() {
			var world = new World();
			int countryEntity = SeedCountry(world, "A");
			SeedProvince(world, "prov_1", "A", 1000);

			CountryScoreSystem.Recompute(world, 0.01);

			int[] countryRequired = { TypeId<Country>.Value };
			int[] countryAndScoreRequired = { TypeId<Country>.Value, TypeId<Score>.Value };
			bool foundScoredEntity = false;
			foreach (Archetype arch in world.GetMatchingArchetypes(countryAndScoreRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (countries[i].CountryId == "A") {
						Assert.Equal(countryEntity, arch.Entities[i]);
						foundScoredEntity = true;
					}
				}
			}
			Assert.True(foundScoredEntity);

			int countryOnlyEntityCount = 0;
			foreach (Archetype arch in world.GetMatchingArchetypes(countryRequired, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (countries[i].CountryId == "A") {
						countryOnlyEntityCount++;
					}
				}
			}
			Assert.Equal(1, countryOnlyEntityCount);
		}
	}
}
