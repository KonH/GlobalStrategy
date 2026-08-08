using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceQueryTests {
		[Fact]
		void get_value_returns_matching_owner_and_resource_id() {
			var world = new World();
			var resources = new ResourceQuery();
			resources.Set(world, "A", "country_population", 1234.0, OwnerType.Country);

			double value = resources.GetValue(world, "A", "country_population");

			Assert.Equal(1234.0, value);
		}

		[Fact]
		void get_value_returns_zero_when_not_found() {
			var world = new World();
			var resources = new ResourceQuery();

			double value = resources.GetValue(world, "Unknown", "country_population");

			Assert.Equal(0.0, value);
		}

		[Fact]
		void set_updates_cache_and_world() {
			var world = new World();
			var resources = new ResourceQuery();
			resources.Set(world, "OrgA", "gold", 10);

			Assert.Equal(10, resources.GetValue(world, "OrgA", "gold"));
			resources.TryUpdate(world, "OrgA", "gold", 25, out double oldValue);

			Assert.Equal(10, oldValue);
			Assert.Equal(25, resources.GetValue(world, "OrgA", "gold"));
		}

		[Fact]
		void remove_destroys_transient_resource_and_clears_cache() {
			var world = new World();
			var resources = new ResourceQuery();
			resources.Set(world, "war_1", "war_progress", 5, OwnerType.War);

			Assert.True(resources.Remove(world, "war_1", "war_progress"));
			Assert.Equal(0, resources.GetValue(world, "war_1", "war_progress"));
			Assert.Equal(-1, resources.FindEntity(world, "war_1", "war_progress"));
		}

		[Fact]
		void cache_miss_falls_back_to_world_and_warns() {
			var world = new World();
			var resources = new ResourceQuery();
			string? warning = null;
			resources.OnCacheMissWarning = message => warning = message;

			int entity = world.Create();
			world.Add(entity, new ResourceOwner("A", OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = "gold", Value = 7 });

			Assert.Equal(7, resources.GetValue(world, "A", "gold"));
			Assert.Contains("cache miss", warning);
			Assert.Equal(7, resources.GetValue(world, "A", "gold"));
		}
	}
}
