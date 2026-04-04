using ECS.Extensions;
using Xunit;

namespace ECS.Tests {
	public class ExtensionsTests {
		[Fact]
		void get_or_add_returns_default_when_absent() {
			var world = new World();
			int e = world.Create();
			ref Health h = ref world.GetOrAdd<Health>(e, new Health { Value = 5 });
			Assert.Equal(5, h.Value);
			Assert.True(world.Has<Health>(e));
		}

		[Fact]
		void get_or_add_returns_existing_when_present() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Health { Value = 99 });
			ref Health h = ref world.GetOrAdd<Health>(e, new Health { Value = 0 });
			Assert.Equal(99, h.Value);
		}

		[Fact]
		void add_range_adds_to_all_entities() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			int c = world.Create();

			world.AddRange<Tag>(new int[] { a, b, c }, new Tag());

			Assert.True(world.Has<Tag>(a));
			Assert.True(world.Has<Tag>(b));
			Assert.True(world.Has<Tag>(c));
		}

		[Fact]
		void destroy_all_removes_every_entity() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			world.Add(a, new Position { X = 1 });
			world.Add(b, new Position { X = 2 });

			world.DestroyAll();

			Assert.False(world.IsAlive(a));
			Assert.False(world.IsAlive(b));
		}
	}
}
