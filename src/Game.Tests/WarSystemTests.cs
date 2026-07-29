using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1  = new DateTime(1880, 2,  1,  0, 0, 0);
		static readonly DateTime Jan1  = new DateTime(1880, 1,  1,  0, 0, 0);
		static readonly DateTime Jan2  = new DateTime(1880, 1,  2,  0, 0, 0);

		static int AddWarProgress(World world, double value) {
			int e = world.Create();
			world.Add(e, new War { WarId = "war_A_B_1" });
			world.Add(e, new WarProgress { Value = value });
			return e;
		}

		[Fact]
		void month_boundary_decays_progress_by_configured_amount() {
			var world = new World();
			int e = AddWarProgress(world, 0);

			WarSystem.Update(world, Jan31, Feb1, 2.5);

			Assert.Equal(-2.5, world.Get<WarProgress>(e).Value);
		}

		[Fact]
		void decay_floor_clamps_at_minus_100_and_does_not_go_lower() {
			var world = new World();
			int e = AddWarProgress(world, -99);

			WarSystem.Update(world, Jan31, Feb1, 2.5);
			Assert.Equal(-100, world.Get<WarProgress>(e).Value);

			WarSystem.Update(world, new DateTime(1880, 2, 28), new DateTime(1880, 3, 1), 2.5);
			Assert.Equal(-100, world.Get<WarProgress>(e).Value);
		}

		[Fact]
		void no_month_boundary_means_no_change() {
			var world = new World();
			int e = AddWarProgress(world, 10);

			WarSystem.Update(world, Jan1, Jan2, 2.5);

			Assert.Equal(10, world.Get<WarProgress>(e).Value);
		}
	}
}
