using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class OrgScoreTests {
		static int SeedCountry(World world, string countryId, double score) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			world.Add(entity, new Score { Value = score });
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
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org1", "B", 50);

			Assert.Equal(65.0, OrgScore.GetScore(world, "Org1"));
		}

		[Fact]
		void org_with_no_control_anywhere_scores_zero() {
			var world = new World();
			SeedCountry(world, "A", 200);

			Assert.Equal(0.0, OrgScore.GetScore(world, "Org1"));
		}

		[Fact]
		void control_in_zero_score_countries_scores_zero() {
			var world = new World();
			SeedCountry(world, "A", 0);
			AddControl(world, "Org1", "A", 50);
			AddControl(world, "Org1", "B", 50); // no Score entity at all for B

			Assert.Equal(0.0, OrgScore.GetScore(world, "Org1"));
		}

		[Fact]
		void multiple_control_effects_in_one_country_sum_before_weighting() {
			var world = new World();
			SeedCountry(world, "A", 100);
			AddControl(world, "Org1", "A", 10, "base_Org1");
			AddControl(world, "Org1", "A", 20, "permanent_Org1_A");

			Assert.Equal(30.0, OrgScore.GetScore(world, "Org1"));
		}

		[Fact]
		void control_effects_belonging_to_other_orgs_are_excluded() {
			var world = new World();
			SeedCountry(world, "A", 100);
			AddControl(world, "Org1", "A", 30);
			AddControl(world, "Org2", "A", 40);

			Assert.Equal(30.0, OrgScore.GetScore(world, "Org1"));
		}

		[Fact]
		void get_score_is_pure_and_repeatable() {
			var world = new World();
			SeedCountry(world, "A", 100);
			AddControl(world, "Org1", "A", 30);

			double first = OrgScore.GetScore(world, "Org1");
			double second = OrgScore.GetScore(world, "Org1");

			Assert.Equal(first, second);

			int[] countryRequired = { TypeId<Country>.Value };
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			int[] scoreRequired = { TypeId<Score>.Value };
			int countryCount = 0;
			int controlCount = 0;
			int scoreCount = 0;
			foreach (var arch in world.GetMatchingArchetypes(countryRequired, null)) {
				countryCount += arch.Count;
			}
			foreach (var arch in world.GetMatchingArchetypes(controlRequired, null)) {
				controlCount += arch.Count;
			}
			foreach (var arch in world.GetMatchingArchetypes(scoreRequired, null)) {
				scoreCount += arch.Count;
			}
			Assert.Equal(1, countryCount);
			Assert.Equal(1, controlCount);
			Assert.Equal(1, scoreCount);
		}
	}
}
