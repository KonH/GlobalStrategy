using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterLeaderboardTests {
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

			var state = new VisualState();
			var converter = new VisualStateConverter(state, countryConfig: BuildCountryConfig());
			converter.UpdateLeaderboards(world);

			Assert.Equal("org_high", state.Leaderboard.Organizations[0].EntityId);
			Assert.Equal(1, state.Leaderboard.Organizations[0].Place);
			Assert.Equal("org_low", state.Leaderboard.Organizations[1].EntityId);
			Assert.Equal(2, state.Leaderboard.Organizations[1].Place);
			Assert.Equal("c_beta", state.Leaderboard.Countries[0].EntityId);
			Assert.Equal(1, state.Leaderboard.Countries[0].Place);
			Assert.Equal("c_alpha", state.Leaderboard.Countries[1].EntityId);
			Assert.Equal(2, state.Leaderboard.Countries[1].Place);
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

			var state = new VisualState();
			var converter = new VisualStateConverter(state, countryConfig: BuildCountryConfig());
			converter.UpdateLeaderboards(world);

			Assert.Equal(new[] { "org_b", "org_a", "org_z" }, new[] {
				state.Leaderboard.Organizations[0].EntityId,
				state.Leaderboard.Organizations[1].EntityId,
				state.Leaderboard.Organizations[2].EntityId
			});
			Assert.Equal(new[] { "c_alpha", "c_beta", "c_gamma" }, new[] {
				state.Leaderboard.Countries[0].EntityId,
				state.Leaderboard.Countries[1].EntityId,
				state.Leaderboard.Countries[2].EntityId
			});
		}

		[Fact]
		void country_score_state_uses_country_score_query_for_all_country_entities() {
			var world = new World();
			SeedCountry(world, "c_alpha", 20.0);
			SeedCountry(world, "c_beta", 50.0);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, countryConfig: BuildCountryConfig());
			converter.UpdateCountryScore(world);

			Assert.Equal(20.0, state.CountryScore.ScoreByCountryId["c_alpha"]);
			Assert.Equal(50.0, state.CountryScore.ScoreByCountryId["c_beta"]);
		}
	}
}
