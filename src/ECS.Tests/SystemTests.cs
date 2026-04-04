using Xunit;

namespace ECS.Tests {
	// Plain system structs — no interface, no registration, no runner indirection.
	// Dependencies are explicit parameters; grouping is plain C# composition.

	struct MovementSystem {
		public void Update(World world, float dt) {
			world.Query<Position, Velocity>(
				(int e, ref Position p, ref Velocity v) => {
					p.X += v.X * dt;
					p.Y += v.Y * dt;
				});
		}
	}

	struct TickSystem {
		public int FrameCount;

		public void Update(World world) {
			FrameCount++;
		}
	}

	// Example of manual context grouping — dependency order is explicit in Update.
	struct SimulationContext {
		public MovementSystem Movement;
		public TickSystem Tick;

		public void Update(World world, float dt) {
			Movement.Update(world, dt);
			Tick.Update(world);
		}
	}

	public class SystemTests {
		[Fact]
		void system_update_increments_own_state() {
			var world = new World();
			var sys = new TickSystem { FrameCount = 0 };

			sys.Update(world);
			sys.Update(world);
			sys.Update(world);

			Assert.Equal(3, sys.FrameCount);
		}

		[Fact]
		void movement_system_applies_velocity() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 0, Y = 0 });
			world.Add(e, new Velocity { X = 5, Y = 3 });

			var sys = new MovementSystem();
			sys.Update(world, dt: 1f);

			Assert.Equal(5f, world.Get<Position>(e).X);
			Assert.Equal(3f, world.Get<Position>(e).Y);
		}

		[Fact]
		void movement_system_scales_by_dt() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 0, Y = 0 });
			world.Add(e, new Velocity { X = 10, Y = 0 });

			var sys = new MovementSystem();
			sys.Update(world, dt: 0.5f);

			Assert.Equal(5f, world.Get<Position>(e).X);
		}

		[Fact]
		void context_runs_systems_in_order() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 0, Y = 0 });
			world.Add(e, new Velocity { X = 1, Y = 0 });

			var ctx = new SimulationContext();
			ctx.Update(world, dt: 1f);
			ctx.Update(world, dt: 1f);

			Assert.Equal(2f, world.Get<Position>(e).X);
			Assert.Equal(2, ctx.Tick.FrameCount);
		}
	}
}
