using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class OrgScoreSystemTests {
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1);
		static readonly DateTime Jan1Noon = new DateTime(1880, 1, 1, 12, 0, 0);
		static readonly DateTime Jan2 = new DateTime(1880, 1, 2);
		static int SeedCountry(World world, string countryId, double score) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			world.Add(entity, new Score { Value = score });
			return entity;
		}

		static int SeedOrg(World world, string orgId) {
			int entity = world.Create();
			world.Add(entity, new Organization { OrganizationId = orgId, DisplayName = orgId });
			return entity;
		}

		static int AddControl(World world, string orgId, string countryId, int value, string? effectId = null) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId     = orgId,
				CountryId = countryId,
				Value     = value,
				EffectId  = effectId ?? $"base_{orgId}"
			});
			return e;
		}

		[Fact]
		void org_score_is_control_fraction_times_country_score_summed() {
			var world = new World();
			SeedCountry(world, "A", 200);
			SeedCountry(world, "B", 10);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org1", "B", 50);

			OrgScoreSystem.Recompute(world);

			Assert.Equal(65.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void org_with_no_control_anywhere_scores_zero() {
			var world = new World();
			SeedCountry(world, "A", 200);
			SeedOrg(world, "Org1");

			OrgScoreSystem.Recompute(world);

			Assert.Equal(0.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void control_in_zero_score_countries_scores_zero() {
			var world = new World();
			SeedCountry(world, "A", 0);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 50);
			AddControl(world, "Org1", "B", 50); // no Score entity at all for B

			OrgScoreSystem.Recompute(world);

			Assert.Equal(0.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void multiple_control_effects_in_one_country_sum_before_weighting() {
			var world = new World();
			SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 10, "base_Org1");
			AddControl(world, "Org1", "A", 20, "permanent_Org1_A");

			OrgScoreSystem.Recompute(world);

			Assert.Equal(30.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void control_effects_belonging_to_other_orgs_are_excluded() {
			var world = new World();
			SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			SeedOrg(world, "Org2");
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org2", "A", 40);

			OrgScoreSystem.Recompute(world);

			Assert.Equal(30.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void get_score_is_pure_and_repeatable() {
			var world = new World();
			SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 30);

			OrgScoreSystem.Recompute(world);

			double first = OrgScoreSystem.GetScore(world, "Org1");
			double second = OrgScoreSystem.GetScore(world, "Org1");

			Assert.Equal(first, second);
		}

		[Fact]
		void get_score_returns_zero_for_unknown_org() {
			var world = new World();

			Assert.Equal(0.0, OrgScoreSystem.GetScore(world, "Unknown"));
		}

		[Fact]
		void score_is_composed_onto_the_organization_entity_not_a_separate_entity() {
			var world = new World();
			SeedCountry(world, "A", 100);
			int orgEntity = SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 30);

			OrgScoreSystem.Recompute(world);

			int[] orgRequired = { TypeId<Organization>.Value };
			int[] orgAndScoreRequired = { TypeId<Organization>.Value, TypeId<Score>.Value };
			bool foundScoredEntity = false;
			foreach (Archetype arch in world.GetMatchingArchetypes(orgAndScoreRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrganizationId == "Org1") {
						Assert.Equal(orgEntity, arch.Entities[i]);
						foundScoredEntity = true;
					}
				}
			}
			Assert.True(foundScoredEntity);

			int orgOnlyEntityCount = 0;
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrganizationId == "Org1") {
						orgOnlyEntityCount++;
					}
				}
			}
			Assert.Equal(1, orgOnlyEntityCount);
		}

		[Fact]
		void recompute_reflects_current_country_score_not_stale_value() {
			var world = new World();
			int countryEntity = SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 50);

			OrgScoreSystem.Recompute(world);
			Assert.Equal(50.0, OrgScoreSystem.GetScore(world, "Org1"));

			world.Get<Score>(countryEntity).Value = 200;
			OrgScoreSystem.Recompute(world);

			Assert.Equal(100.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void update_recomputes_on_day_boundary_not_just_month_boundary() {
			var world = new World();
			int countryEntity = SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 50);

			OrgScoreSystem.Update(world, Jan1, Jan1Noon);
			Assert.Equal(0.0, OrgScoreSystem.GetScore(world, "Org1"));

			world.Get<Score>(countryEntity).Value = 200;
			OrgScoreSystem.Update(world, Jan1Noon, Jan2);

			Assert.Equal(100.0, OrgScoreSystem.GetScore(world, "Org1"));
		}

		[Fact]
		void update_is_unchanged_within_same_day() {
			var world = new World();
			int countryEntity = SeedCountry(world, "A", 100);
			SeedOrg(world, "Org1");
			AddControl(world, "Org1", "A", 50);

			OrgScoreSystem.Recompute(world);
			double before = OrgScoreSystem.GetScore(world, "Org1");

			world.Get<Score>(countryEntity).Value = 999;
			OrgScoreSystem.Update(world, Jan1, Jan1Noon);

			Assert.Equal(before, OrgScoreSystem.GetScore(world, "Org1"));
		}
	}
}
