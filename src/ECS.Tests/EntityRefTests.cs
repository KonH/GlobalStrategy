using Xunit;

namespace ECS.Tests {
	public class EntityRefTests {
		[Fact]
		public void ImplicitFromInt_RoundTrips() {
			EntityRef r = 42;
			Assert.Equal(42, r.Id);
		}

		[Fact]
		public void ImplicitToInt_RoundTrips() {
			var r = new EntityRef(99);
			int id = r;
			Assert.Equal(99, id);
		}

		[Fact]
		public void WorldAddAndGet_ComponentWithEntityRefField() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			world.Add(b, new RefHolder { Target = new EntityRef(a) });
			ref RefHolder held = ref world.Get<RefHolder>(b);
			Assert.Equal(a, held.Target.Id);
		}

		struct RefHolder {
			public EntityRef Target;
		}
	}
}
