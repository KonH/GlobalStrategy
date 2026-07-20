using System;
using ECS;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceCollectorRegistryTests {
		sealed class StubCollector : IResourceCollector {
			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) => 0.0;
		}

		[Fact]
		void resolve_returns_registered_collector() {
			var registry = new ResourceCollectorRegistry();
			var collector = new StubCollector();
			registry.Register("stub", collector);

			Assert.Same(collector, registry.Resolve("stub"));
		}

		[Fact]
		void resolve_throws_for_unknown_collector_id() {
			var registry = new ResourceCollectorRegistry();

			Assert.Throws<InvalidOperationException>(() => registry.Resolve("unknown"));
		}
	}
}
