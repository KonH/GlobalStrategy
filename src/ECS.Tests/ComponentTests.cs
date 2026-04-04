using Xunit;

namespace ECS.Tests {
	public class ComponentTests {
		[Fact]
		void add_and_has() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 1, Y = 2 });
			Assert.True(world.Has<Position>(e));
		}

		[Fact]
		void has_returns_false_before_add() {
			var world = new World();
			int e = world.Create();
			Assert.False(world.Has<Position>(e));
		}

		[Fact]
		void get_returns_correct_value() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 3, Y = 4 });
			ref Position p = ref world.Get<Position>(e);
			Assert.Equal(3f, p.X);
			Assert.Equal(4f, p.Y);
		}

		[Fact]
		void get_ref_mutation_persists() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 0, Y = 0 });
			world.Get<Position>(e).X = 99;
			Assert.Equal(99f, world.Get<Position>(e).X);
		}

		[Fact]
		void try_get_present_returns_true() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Health { Value = 10 });
			bool found = world.TryGet<Health>(e, out Health h);
			Assert.True(found);
			Assert.Equal(10, h.Value);
		}

		[Fact]
		void try_get_absent_returns_false() {
			var world = new World();
			int e = world.Create();
			bool found = world.TryGet<Health>(e, out Health h);
			Assert.False(found);
			Assert.Equal(default, h.Value);
		}

		[Fact]
		void remove_clears_has() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 1, Y = 1 });
			world.Remove<Position>(e);
			Assert.False(world.Has<Position>(e));
		}

		[Fact]
		void add_multiple_components() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 1, Y = 2 });
			world.Add(e, new Velocity { X = 3, Y = 4 });
			Assert.True(world.Has<Position>(e));
			Assert.True(world.Has<Velocity>(e));
			Assert.Equal(1f, world.Get<Position>(e).X);
			Assert.Equal(3f, world.Get<Velocity>(e).X);
		}

		[Fact]
		void archetype_transitions_correct_after_add_remove() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 5, Y = 6 });
			world.Add(e, new Velocity { X = 1, Y = 0 });

			// Remove Position; Velocity should still be intact.
			world.Remove<Position>(e);
			Assert.False(world.Has<Position>(e));
			Assert.True(world.Has<Velocity>(e));
			Assert.Equal(1f, world.Get<Velocity>(e).X);
		}

		[Fact]
		void swap_remove_updates_moved_entity_row() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			world.Add(a, new Position { X = 1 });
			world.Add(b, new Position { X = 2 });

			// Destroy a → b gets swap-removed to row 0
			world.Destroy(a);

			Assert.True(world.IsAlive(b));
			Assert.Equal(2f, world.Get<Position>(b).X);
		}

		[Fact]
		void stale_entity_try_get_returns_false() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 1 });
			world.Destroy(e);
			bool found = world.TryGet<Position>(e, out _);
			Assert.False(found);
		}
	}
}
