using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class RevengeWarBonusQueryTests {
		static int AddBonus(World world, string countryId, double damagePercent, double durabilityPercent) {
			int e = world.Create();
			world.Add(e, new RevengeWarBonus {
				WarId = $"war_{countryId}",
				CountryId = countryId,
				DamageBonusPercent = damagePercent,
				DurabilityBonusPercent = durabilityPercent
			});
			return e;
		}

		[Fact]
		void get_bonus_percent_returns_zero_when_no_bonus_exists() {
			var world = new World();

			Assert.Equal(0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "damage"));
			Assert.Equal(0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "durability"));
		}

		[Fact]
		void get_bonus_percent_returns_matching_country_damage_and_durability() {
			var world = new World();
			AddBonus(world, "Great_Britain", 10.0, 5.0);

			Assert.Equal(10.0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "damage"));
			Assert.Equal(5.0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "durability"));
		}

		[Fact]
		void get_bonus_percent_unaffected_by_other_countries_bonuses() {
			var world = new World();
			AddBonus(world, "France", 10.0, 5.0);

			Assert.Equal(0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "damage"));
			Assert.Equal(0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "durability"));
		}

		[Fact]
		void remove_for_country_destroys_only_the_matching_countrys_bonus() {
			var world = new World();
			int gbBonus = AddBonus(world, "Great_Britain", 10.0, 5.0);
			int frBonus = AddBonus(world, "France", 20.0, 15.0);

			RevengeWarBonusQuery.RemoveForCountry(world, "Great_Britain");

			Assert.False(world.IsAlive(gbBonus));
			Assert.True(world.IsAlive(frBonus));
			Assert.Equal(0, RevengeWarBonusQuery.GetBonusPercent(world, "Great_Britain", "damage"));
			Assert.Equal(20.0, RevengeWarBonusQuery.GetBonusPercent(world, "France", "damage"));
		}

		[Fact]
		void remove_for_country_on_country_with_no_bonus_is_a_no_op() {
			var world = new World();
			int frBonus = AddBonus(world, "France", 20.0, 15.0);

			RevengeWarBonusQuery.RemoveForCountry(world, "Great_Britain");

			Assert.True(world.IsAlive(frBonus));
		}
	}
}
