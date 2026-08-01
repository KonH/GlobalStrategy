using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class RevengeWarBonusDecaySystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1  = new DateTime(1880, 2,  1,  0, 0, 0);
		static readonly DateTime Jan1  = new DateTime(1880, 1,  1,  0, 0, 0);
		static readonly DateTime Jan2  = new DateTime(1880, 1,  2,  0, 0, 0);

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
		void month_boundary_decays_both_percentages_by_configured_amounts() {
			var world = new World();
			int e = AddBonus(world, "Great_Britain", 10.0, 5.0);

			RevengeWarBonusDecaySystem.Update(world, Jan31, Feb1, 1.0, 0.5);

			Assert.Equal(9.0, world.Get<RevengeWarBonus>(e).DamageBonusPercent);
			Assert.Equal(4.5, world.Get<RevengeWarBonus>(e).DurabilityBonusPercent);
		}

		[Fact]
		void decay_floor_clamps_at_zero_and_stays_there_on_a_later_boundary() {
			var world = new World();
			int e = AddBonus(world, "Great_Britain", 0.5, 0.25);

			RevengeWarBonusDecaySystem.Update(world, Jan31, Feb1, 1.0, 0.5);
			Assert.Equal(0, world.Get<RevengeWarBonus>(e).DamageBonusPercent);
			Assert.Equal(0, world.Get<RevengeWarBonus>(e).DurabilityBonusPercent);

			RevengeWarBonusDecaySystem.Update(world, new DateTime(1880, 2, 28), new DateTime(1880, 3, 1), 1.0, 0.5);
			Assert.Equal(0, world.Get<RevengeWarBonus>(e).DamageBonusPercent);
			Assert.Equal(0, world.Get<RevengeWarBonus>(e).DurabilityBonusPercent);
		}

		[Fact]
		void no_month_boundary_means_no_change() {
			var world = new World();
			int e = AddBonus(world, "Great_Britain", 10.0, 5.0);

			RevengeWarBonusDecaySystem.Update(world, Jan1, Jan2, 1.0, 0.5);

			Assert.Equal(10.0, world.Get<RevengeWarBonus>(e).DamageBonusPercent);
			Assert.Equal(5.0, world.Get<RevengeWarBonus>(e).DurabilityBonusPercent);
		}

		[Fact]
		void two_bonus_entities_decay_independently() {
			var world = new World();
			int gb = AddBonus(world, "Great_Britain", 10.0, 5.0);
			int fr = AddBonus(world, "France", 2.0, 1.0);

			RevengeWarBonusDecaySystem.Update(world, Jan31, Feb1, 1.0, 0.5);

			Assert.Equal(9.0, world.Get<RevengeWarBonus>(gb).DamageBonusPercent);
			Assert.Equal(4.5, world.Get<RevengeWarBonus>(gb).DurabilityBonusPercent);
			Assert.Equal(1.0, world.Get<RevengeWarBonus>(fr).DamageBonusPercent);
			Assert.Equal(0.5, world.Get<RevengeWarBonus>(fr).DurabilityBonusPercent);
		}
	}
}
