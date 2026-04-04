using Xunit;

namespace ECS.Tests {
	public class EntityTests {
		[Fact]
		void create_returns_distinct_ids() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			Assert.NotEqual(a, b);
		}

		[Fact]
		void created_entity_is_alive() {
			var world = new World();
			int e = world.Create();
			Assert.True(world.IsAlive(e));
		}

		[Fact]
		void destroyed_entity_is_not_alive() {
			var world = new World();
			int e = world.Create();
			world.Destroy(e);
			Assert.False(world.IsAlive(e));
		}

		[Fact]
		void destroy_twice_is_safe() {
			var world = new World();
			int e = world.Create();
			world.Destroy(e);
			world.Destroy(e); // should not throw
		}

		[Fact]
		void reused_index_gets_new_generation() {
			var world = new World();
			int e1 = world.Create();
			world.Destroy(e1);
			int e2 = world.Create();
			// Same underlying index but different generation → not alive under old id
			Assert.False(world.IsAlive(e1));
			Assert.True(world.IsAlive(e2));
		}

		[Fact]
		void generation_wrap_old_id_not_alive() {
			// Cycle the same slot 4095 times so the generation reaches its max value (4095).
			// The original entity had gen=0; the final one has gen=4095 → still distinct.
			// A full 4096-cycle wrap is intentionally undefined (same packed id reappears).
			var world = new World();
			int first = world.Create(); // gen = 0
			int last = first;
			for (int i = 0; i < 4095; i++) {
				world.Destroy(last);
				last = world.Create();
			}
			// first (gen 0) ≠ last (gen 4095) → first is not alive
			Assert.False(world.IsAlive(first));
			Assert.True(world.IsAlive(last));
		}
	}
}
