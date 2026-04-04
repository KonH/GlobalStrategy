using System.Collections.Generic;
using Xunit;

namespace ECS.Tests {
	public class QueryTests {
		[Fact]
		void query_single_component_visits_all_matching() {
			var world = new World();
			int a = world.Create(); world.Add(a, new Position { X = 1 });
			int b = world.Create(); world.Add(b, new Position { X = 2 });
			int c = world.Create(); // no Position

			var visited = new List<int>();
			world.Query<Position>((int e, ref Position p) => visited.Add(e));

			Assert.Contains(a, visited);
			Assert.Contains(b, visited);
			Assert.DoesNotContain(c, visited);
		}

		[Fact]
		void query_multi_component_visits_only_entities_with_all() {
			var world = new World();
			int both = world.Create();
			world.Add(both, new Position { X = 1 });
			world.Add(both, new Velocity { X = 1 });

			int posOnly = world.Create();
			world.Add(posOnly, new Position { X = 2 });

			int visited = 0;
			world.Query<Position, Velocity>(
				(int e, ref Position p, ref Velocity v) => visited++);

			Assert.Equal(1, visited);
		}

		[Fact]
		void query_callback_can_mutate_components() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 0, Y = 0 });
			world.Add(e, new Velocity { X = 2, Y = 3 });

			world.Query<Position, Velocity>(
				static (int id, ref Position p, ref Velocity v) => {
					p.X += v.X;
					p.Y += v.Y;
				});

			Assert.Equal(2f, world.Get<Position>(e).X);
			Assert.Equal(3f, world.Get<Position>(e).Y);
		}

		[Fact]
		void query_builder_exclude_skips_excluded_archetype() {
			var world = new World();
			int moving = world.Create();
			world.Add(moving, new Position { X = 1 });
			world.Add(moving, new Velocity { X = 1 });

			int frozen = world.Create();
			world.Add(frozen, new Position { X = 2 });
			world.Add(frozen, new Velocity { X = 1 });
			world.Add(frozen, new Frozen());

			var visited = new List<int>();
			world.Query<Position, Velocity>()
				.Exclude<Frozen>()
				.Run((int e, ref Position p, ref Velocity v) => visited.Add(e));

			Assert.Contains(moving, visited);
			Assert.DoesNotContain(frozen, visited);
		}

		[Fact]
		void query_three_components() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Position { X = 1 });
			world.Add(e, new Velocity { X = 2 });
			world.Add(e, new Health { Value = 10 });

			int count = 0;
			world.Query<Position, Velocity, Health>(
				(int id, ref Position p, ref Velocity v, ref Health h) => count++);

			Assert.Equal(1, count);
		}

		[Fact]
		void query_empty_world_visits_nothing() {
			var world = new World();
			int count = 0;
			world.Query<Position>((int e, ref Position p) => count++);
			Assert.Equal(0, count);
		}
	}
}
