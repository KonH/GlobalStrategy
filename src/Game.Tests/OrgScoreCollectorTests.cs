using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class OrgScoreCollectorTests {
		static void SeedCountryScore(World world, string countryId, double score) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.CountryScore, Value = score });
		}

		static void AddControl(World world, string orgId, string countryId, int value, string? effectId = null) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId     = orgId,
				CountryId = countryId,
				Value     = value,
				EffectId  = effectId ?? $"base_{orgId}"
			});
		}

		[Fact]
		void compute_returns_control_fraction_times_country_score_summed() {
			var world = new World();
			SeedCountryScore(world, "A", 200);
			SeedCountryScore(world, "B", 10);
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org1", "B", 50);
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 0.0, world);

			Assert.Equal(65.0, delta, 6);
		}

		[Fact]
		void org_with_no_control_anywhere_scores_zero() {
			var world = new World();
			SeedCountryScore(world, "A", 200);
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 0.0, world);

			Assert.Equal(0.0, delta);
		}

		[Fact]
		void control_in_countries_with_no_score_resource_contributes_zero() {
			var world = new World();
			AddControl(world, "Org1", "A", 50); // no Resource entity at all for A
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 0.0, world);

			Assert.Equal(0.0, delta);
		}

		[Fact]
		void multiple_control_effects_in_one_country_sum_before_weighting() {
			var world = new World();
			SeedCountryScore(world, "A", 100);
			AddControl(world, "Org1", "A", 10, "base_Org1");
			AddControl(world, "Org1", "A", 20, "permanent_Org1_A");
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 0.0, world);

			Assert.Equal(30.0, delta, 6);
		}

		[Fact]
		void control_effects_belonging_to_other_orgs_are_excluded() {
			var world = new World();
			SeedCountryScore(world, "A", 100);
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org2", "A", 40);
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 0.0, world);

			Assert.Equal(30.0, delta, 6);
		}

		[Fact]
		void compute_returns_delta_from_current_value() {
			var world = new World();
			SeedCountryScore(world, "A", 100);
			AddControl(world, "Org1", "A", 50);
			var collector = new OrgScoreCollector();

			double delta = collector.Compute("Org1", 40.0, world);

			Assert.Equal(10.0, delta, 6);
		}
	}
}
