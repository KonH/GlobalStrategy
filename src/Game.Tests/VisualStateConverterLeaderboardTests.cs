using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	public class VisualStateConverterLeaderboardTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static int SeedCountry(World world, string countryId, double score) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			world.Add(entity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.CountryScore, Value = score });
			return entity;
		}

		static int SeedOrganization(World world, string orgId, string displayName, double score) {
			int entity = world.Create();
			world.Add(entity, new Organization { OrganizationId = orgId, DisplayName = displayName });
			world.Add(entity, new ResourceOwner(orgId, OwnerType.Org));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.OrgScore, Value = score });
			return entity;
		}

		static CountryConfig BuildCountryConfig() => new CountryConfig {
			Countries = new List<CountryEntry> {
				new CountryEntry { CountryId = "c_alpha", DisplayName = "Alpha", IsAvailable = true },
				new CountryEntry { CountryId = "c_beta", DisplayName = "Beta", IsAvailable = true },
				new CountryEntry { CountryId = "c_gamma", DisplayName = "Gamma", IsAvailable = true }
			}
		};

		[Fact]
		void leaderboards_are_sorted_by_score_descending_and_place_numbered() {
			var world = new World();
			SeedOrganization(world, "org_low", "Low", 10.0);
			SeedOrganization(world, "org_high", "High", 30.0);
			SeedCountry(world, "c_alpha", 20.0);
			SeedCountry(world, "c_beta", 50.0);

			var state = new LeaderboardState();
			LeaderboardProjector.Project(world, state, _resources, BuildCountryConfig());

			Assert.Equal("org_high", state.Organizations[0].EntityId);
			Assert.Equal(1, state.Organizations[0].Place);
			Assert.Equal("org_low", state.Organizations[1].EntityId);
			Assert.Equal(2, state.Organizations[1].Place);
			Assert.Equal("c_beta", state.Countries[0].EntityId);
			Assert.Equal(1, state.Countries[0].Place);
			Assert.Equal("c_alpha", state.Countries[1].EntityId);
			Assert.Equal(2, state.Countries[1].Place);
		}

		[Fact]
		void leaderboards_break_ties_by_display_name_then_id() {
			var world = new World();
			SeedOrganization(world, "org_z", "Same", 10.0);
			SeedOrganization(world, "org_a", "Same", 10.0);
			SeedOrganization(world, "org_b", "Alpha", 10.0);
			SeedCountry(world, "c_gamma", 25.0);
			SeedCountry(world, "c_beta", 25.0);
			SeedCountry(world, "c_alpha", 25.0);

			var state = new LeaderboardState();
			LeaderboardProjector.Project(world, state, _resources, BuildCountryConfig());

			Assert.Equal(new[] { "org_b", "org_a", "org_z" }, new[] {
				state.Organizations[0].EntityId,
				state.Organizations[1].EntityId,
				state.Organizations[2].EntityId
			});
			Assert.Equal(new[] { "c_alpha", "c_beta", "c_gamma" }, new[] {
				state.Countries[0].EntityId,
				state.Countries[1].EntityId,
				state.Countries[2].EntityId
			});
		}

		[Fact]
		void large_world_projects_every_country_and_org_with_contiguous_places() {
			var world = new World();
			const int countryCount = 154;
			const int orgCount = 8;
			for (int i = 0; i < countryCount; i++) {
				SeedCountry(world, $"c_{i:000}", countryCount - i);
			}
			for (int i = 0; i < orgCount; i++) {
				SeedOrganization(world, $"org_{i}", $"Org {i}", orgCount - i);
			}

			var state = new LeaderboardState();
			LeaderboardProjector.Project(world, state, _resources, null);

			Assert.Equal(countryCount, state.Countries.Count);
			Assert.Equal(orgCount, state.Organizations.Count);
			for (int i = 0; i < state.Countries.Count; i++) {
				Assert.Equal(i + 1, state.Countries[i].Place);
			}
			for (int i = 0; i < state.Organizations.Count; i++) {
				Assert.Equal(i + 1, state.Organizations[i].Place);
			}
			// Highest-score country/org seeded first stays first after sorting descending.
			Assert.Equal("c_000", state.Countries[0].EntityId);
			Assert.Equal("org_0", state.Organizations[0].EntityId);
		}

		[Fact]
		void country_score_state_uses_country_score_query_for_all_country_entities() {
			var world = new World();
			SeedCountry(world, "c_alpha", 20.0);
			SeedCountry(world, "c_beta", 50.0);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations, countryConfig: BuildCountryConfig());
			converter.UpdateCountryScore(world);

			Assert.Equal(20.0, state.CountryScore.ScoreByCountryId["c_alpha"]);
			Assert.Equal(50.0, state.CountryScore.ScoreByCountryId["c_beta"]);
		}
	}
}
