using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceMutationsTests {
		[Fact]
		void mutations_distinguish_missing_resources_and_clamp_deltas() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("France", OwnerType.Country));
			world.Add(entity, new Resource { ResourceId = "recruits", Value = 10 });

			Assert.True(ResourceQuery.TryGetValue(world, "France", "recruits", out double initial));
			Assert.Equal(10, initial);
			Assert.False(ResourceQuery.TryGetValue(world, "France", "missing", out _));

			Assert.True(ResourceMutations.TryApplyClampedDelta(
				world, "France", "recruits", -15, 0, double.MaxValue, out double applied));
			Assert.Equal(-10, applied);
			Assert.Equal(0, ResourceQuery.GetValue(world, "France", "recruits"));

			Assert.True(ResourceMutations.TrySetValue(world, "France", "recruits", 7, out double oldValue));
			Assert.Equal(0, oldValue);
			Assert.Equal(7, ResourceQuery.GetValue(world, "France", "recruits"));
		}
	}
}
