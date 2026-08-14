using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarWinChanceEstimatorTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		void SetResource(World world, string countryId, string resourceId, double value) {
			_resources.Set(world, countryId, resourceId, value, OwnerType.Country);
		}

		void SetCombatBundle(
			World world,
			string countryId,
			double recruits,
			double damage,
			double durability) {
			SetResource(world, countryId, ResourceDefinitions.Recruits, recruits);
			SetResource(world, countryId, ResourceDefinitions.Damage, damage);
			SetResource(world, countryId, ResourceDefinitions.Durability, durability);
		}

		void AddRevengeBonus(World world, string countryId, double damagePercent, double durabilityPercent) {
			int e = world.Create();
			world.Add(e, new RevengeWarBonus {
				WarId = $"war_{countryId}",
				CountryId = countryId,
				DamageBonusPercent = damagePercent,
				DurabilityBonusPercent = durabilityPercent
			});
		}

		[Fact]
		void equal_strengths_return_50() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 10, damage: 40, durability: 50);
			SetCombatBundle(world, "B", recruits: 10, damage: 40, durability: 50);

			Assert.Equal(50, WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B"));
		}

		[Fact]
		void attacker_zero_recruits_returns_1_even_when_defender_also_zero() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 0, damage: 40, durability: 50);
			SetCombatBundle(world, "B", recruits: 0, damage: 40, durability: 50);

			Assert.Equal(1, WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B"));
		}

		[Fact]
		void attacker_zero_recruits_returns_1_against_strong_defender() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 0, damage: 100, durability: 100);
			SetCombatBundle(world, "B", recruits: 50, damage: 100, durability: 100);

			Assert.Equal(1, WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B"));
		}

		[Fact]
		void strong_attacker_returns_high_green_band_and_never_exceeds_99() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 1000, damage: 200, durability: 100);
			SetCombatBundle(world, "B", recruits: 1, damage: 10, durability: 10);

			int percent = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B");

			Assert.InRange(percent, 67, 99);
			Assert.True(percent <= 99);
			Assert.True(percent >= 1);
		}

		[Fact]
		void weak_attacker_never_drops_below_1() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 1, damage: 10, durability: 10);
			SetCombatBundle(world, "B", recruits: 1000, damage: 200, durability: 100);

			int percent = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B");

			Assert.InRange(percent, 1, 33);
		}

		[Fact]
		void revenge_pending_bonuses_raise_percent_vs_same_live_inputs() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 20, damage: 40, durability: 40);
			SetCombatBundle(world, "B", recruits: 20, damage: 40, durability: 40);

			int withoutPending = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B");
			int withPending = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B", pendingAttackerDamageBonusPercent: 10, pendingAttackerDurabilityBonusPercent: 5);

			Assert.Equal(50, withoutPending);
			Assert.True(withPending > withoutPending);
		}

		[Fact]
		void live_residual_revenge_plus_pending_uses_replace_not_stack() {
			var world = new World();
			// Live damage/durability already include residual revenge 5%/2.5%.
			// Base without residual would be 40 / 40; with residual: 42 / 41.
			SetCombatBundle(world, "A", recruits: 20, damage: 42, durability: 41);
			SetCombatBundle(world, "B", recruits: 20, damage: 40, durability: 40);
			AddRevengeBonus(world, "A", damagePercent: 5.0, durabilityPercent: 2.5);

			int withPending = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B", pendingAttackerDamageBonusPercent: 10, pendingAttackerDurabilityBonusPercent: 5);

			// Same as stripping residual then applying pending 10%/5% on base 40/40.
			var stripped = new World();
			SetCombatBundle(stripped, "A", recruits: 20, damage: 44, durability: 42);
			SetCombatBundle(stripped, "B", recruits: 20, damage: 40, durability: 40);
			int expected = WarWinChanceEstimator.EstimateAttackerWinPercent(stripped, _resources, "A", "B");

			Assert.Equal(expected, withPending);

			// Stacking residual+pending on live would differ (42*1.1, 41*1.05).
			var stacked = new World();
			SetCombatBundle(stacked, "A", recruits: 20, damage: 46.2, durability: 43.05);
			SetCombatBundle(stacked, "B", recruits: 20, damage: 40, durability: 40);
			int stackedPercent = WarWinChanceEstimator.EstimateAttackerWinPercent(stacked, _resources, "A", "B");
			Assert.NotEqual(stackedPercent, withPending);
		}

		[Fact]
		void missing_resources_do_not_throw_and_follow_edge_rules() {
			var world = new World();

			Assert.Equal(1, WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B"));
		}

		[Fact]
		void both_strengths_zero_with_positive_attacker_recruits_returns_50() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 10, damage: 0, durability: 0);
			SetCombatBundle(world, "B", recruits: 10, damage: 0, durability: 0);

			Assert.Equal(50, WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B"));
		}

		[Fact]
		void war_win_chance_numeric_overload_matches_world_path() {
			var world = new World();
			SetCombatBundle(world, "A", recruits: 20, damage: 40, durability: 40);
			SetCombatBundle(world, "B", recruits: 15, damage: 35, durability: 45);

			int worldPlain = WarWinChanceEstimator.EstimateAttackerWinPercent(world, _resources, "A", "B");
			int numericPlain = WarWinChanceEstimator.EstimateAttackerWinPercent(
				20, 40, 40, 15, 35, 45);
			Assert.Equal(worldPlain, numericPlain);

			int worldPending = WarWinChanceEstimator.EstimateAttackerWinPercent(
				world, _resources, "A", "B",
				pendingAttackerDamageBonusPercent: 10,
				pendingAttackerDurabilityBonusPercent: 5);
			int numericPending = WarWinChanceEstimator.EstimateAttackerWinPercent(
				20, 40, 40, 15, 35, 45,
				pendingAttackerDamageBonusPercent: 10,
				pendingAttackerDurabilityBonusPercent: 5);
			Assert.Equal(worldPending, numericPending);
		}
	}
}
