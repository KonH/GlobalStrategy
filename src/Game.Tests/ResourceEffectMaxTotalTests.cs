using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceEffectMaxTotalTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
		static readonly DateTime Mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
		static readonly DateTime Feb28 = new DateTime(1880, 2, 28, 23, 0, 0);

		static World CreateWorldWithResource(string ownerId, string resourceId, double initialValue, out int resourceEntity) {
			var world = new World();
			resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(ownerId));
			world.Add(resourceEntity, new Resource { ResourceId = resourceId, Value = initialValue });
			return world;
		}

		static int AddMonthlyEffect(World world, string ownerId, string resourceId, string effectId, double value, double maxTotal) {
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner(ownerId));
			world.Add(effectEntity, new ResourceLink(resourceId));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = effectId,
				Value = value,
				PayType = PayType.Monthly,
				MaxTotal = maxTotal
			});
			return effectEntity;
		}

		[Fact]
		void resource_effect_stops_applying_when_max_total_reached() {
			var world = CreateWorldWithResource("char1", "opinion_Illuminati", 10.0, out int re);
			AddMonthlyEffect(world, "char1", "opinion_Illuminati", "decay", -5.0, maxTotal: 10.0);

			ResourceSystem.Update(world, Jan31, Feb1);
			Assert.Equal(5.0, world.Get<Resource>(re).Value);

			ResourceSystem.Update(world, Feb28, Mar1);
			Assert.Equal(0.0, world.Get<Resource>(re).Value);

			// Third month: accumulated (10) already equals MaxTotal, no further change.
			DateTime mar31 = new DateTime(1880, 3, 31, 23, 0, 0);
			DateTime apr1 = new DateTime(1880, 4, 1, 0, 0, 0);
			ResourceSystem.Update(world, mar31, apr1);
			Assert.Equal(0.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void resource_effect_applies_normally_when_max_total_zero() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 1.0, maxTotal: 0.0);

			ResourceSystem.Update(world, Jan31, Feb1);
			Assert.Equal(101.0, world.Get<Resource>(re).Value);

			ResourceSystem.Update(world, Feb28, Mar1);
			Assert.Equal(102.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void resource_effect_clamp_to_zero_stops_overshoot_and_removes_effect() {
			// MaxTotal left unbounded (0) so the -5.0 step is not pre-clamped by MaxTotal;
			// ClampToZero must independently stop the value from crossing zero.
			var world = CreateWorldWithResource("char1", "opinion_Illuminati", 3.0, out int re);
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner("char1"));
			world.Add(effectEntity, new ResourceLink("opinion_Illuminati"));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = "decay",
				Value = -5.0,
				PayType = PayType.Monthly,
				ClampToZero = true
			});

			ResourceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(0.0, world.Get<Resource>(re).Value);
			Assert.False(world.IsAlive(effectEntity));
		}
	}
}
