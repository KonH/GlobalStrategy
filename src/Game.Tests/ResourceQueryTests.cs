using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceQueryTests {
		[Fact]
		void get_value_returns_matching_owner_and_resource_id() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("A", OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = "country_population", Value = 1234.0 });

			double value = ResourceQuery.GetValue(world, "A", "country_population");

			Assert.Equal(1234.0, value);
		}

		[Fact]
		void get_value_returns_zero_when_not_found() {
			var world = new World();

			double value = ResourceQuery.GetValue(world, "Unknown", "country_population");

			Assert.Equal(0.0, value);
		}
	}
}
