using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1, 0, 0, 0);
		static readonly DateTime Jan2 = new DateTime(1880, 1, 2, 0, 0, 0);

		sealed class StubFixedDeltaCollector : IResourceCollector {
			readonly double _delta;
			public StubFixedDeltaCollector(double delta) { _delta = delta; }
			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world, ResourceQuery resources) => _delta;
		}

		sealed class MirrorResourceCollector : IResourceCollector {
			readonly string _sourceResourceId;
			public MirrorResourceCollector(string sourceResourceId) { _sourceResourceId = sourceResourceId; }

			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world, ResourceQuery resources) {
				int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resourceColumn = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId == ownerId && resourceColumn[i].ResourceId == _sourceResourceId) {
							return resourceColumn[i].Value - currentValue;
						}
					}
				}
				return -currentValue;
			}
		}

		static TestWorld CreateWorldWithResource(string countryId, string resourceId, double initialValue,
			out int resourceEntity) {
			var world = TestWorld.Create().Resource(countryId, resourceId, initialValue);
			resourceEntity = world.Last;
			return world;
		}

		static int AddEffect(TestWorld world, string countryId, string resourceId,
			string effectId, double value, PayType payType) {
			return world.ResourceEffect(
				countryId, resourceId, value, effectId, OwnerType.Org, payType).Last;
		}

		static int AddMonthlyEffect(TestWorld world, string countryId, string resourceId,
			string effectId, double value) {
			return AddEffect(world, countryId, resourceId, effectId, value, PayType.Monthly);
		}

		static int AddInstantEffect(TestWorld world, string countryId, string resourceId,
			string effectId, double value) {
			return AddEffect(world, countryId, resourceId, effectId, value, PayType.Instant);
		}

		static int AddDailyEffect(TestWorld world, string countryId, string resourceId,
			string effectId, double value) {
			return AddEffect(world, countryId, resourceId, effectId, value, PayType.Daily);
		}

		[Fact]
		void daily_effect_not_applied_within_same_day() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddDailyEffect(world, "Russia", "gold", "income", 1.0);
			ResourceSystem.Update(world, Jan1, new DateTime(1880, 1, 1, 12, 0, 0));
			Assert.Equal(100.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void daily_effect_applied_at_day_boundary() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddDailyEffect(world, "Russia", "gold", "income", 1.0);
			ResourceSystem.Update(world, Jan1, Jan2);
			Assert.Equal(101.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void monthly_effect_not_applied_within_same_month() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 1.0);
			ResourceSystem.Update(world, Jan1, Jan2);
			Assert.Equal(100.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void monthly_effect_applied_at_month_boundary() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 1.0);
			ResourceSystem.Update(world, Jan31, Feb1);
			Assert.Equal(101.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void instant_effect_applied_every_frame_then_removed() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			int effectEntity = AddInstantEffect(world, "Russia", "gold", "bonus", 50.0);
			ResourceSystem.Update(world, Jan1, Jan2);
			Assert.Equal(150.0, world.Get<Resource>(re).Value);
			Assert.False(world.IsAlive(effectEntity));
		}

		[Fact]
		void effect_only_applies_to_matching_country_and_resource() {
			var world = TestWorld.Create();
			int re1 = world.Entity().Last;
			world.Add(re1, new ResourceOwner("Russia"));
			world.Add(re1, new Resource { ResourceId = "gold", Value = 100.0 });

			int re2 = world.Entity().Last;
			world.Add(re2, new ResourceOwner("France"));
			world.Add(re2, new Resource { ResourceId = "gold", Value = 100.0 });

			// Only Russia gets the effect
			AddMonthlyEffect(world, "Russia", "gold", "income", 5.0);
			ResourceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(105.0, world.Get<Resource>(re1).Value);
			Assert.Equal(100.0, world.Get<Resource>(re2).Value);
		}

		[Fact]
		void monthly_effect_persists_across_multiple_months() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 1.0);

			// First month boundary
			ResourceSystem.Update(world, Jan31, Feb1);
			Assert.Equal(101.0, world.Get<Resource>(re).Value);

			// Second month boundary
			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ResourceSystem.Update(world, feb28, mar1);
			Assert.Equal(102.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void collector_tagged_effect_value_recomputed_before_apply() {
			var world = CreateWorldWithResource("Russia", "test_resource", 100.0, out int re);
			int effectEntity = world.Entity().Last;
			world.Add(effectEntity, new ResourceOwner("Russia"));
			world.Add(effectEntity, new ResourceLink("test_resource"));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = "stub",
				Value = 999.0,
				PayType = PayType.Monthly
			});
			world.Add(effectEntity, new ResourceCollector { CollectorId = "stub_add_ten" });

			var registry = new ResourceCollectorRegistry();
			registry.Register("stub_add_ten", new StubFixedDeltaCollector(10.0));

			ResourceSystem.Update(world, Jan31, Feb1, registry, new[] { "test_resource" });

			Assert.Equal(110.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void force_resource_recompute_marker_bypasses_daily_gate_and_is_consumed_once() {
			var world = CreateWorldWithResource("Russia", "test_resource", 100.0, out int re);
			int effectEntity = world.Entity().Last;
			world.Add(effectEntity, new ResourceOwner("Russia"));
			world.Add(effectEntity, new ResourceLink("test_resource"));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = "stub",
				Value = 999.0,
				PayType = PayType.Daily
			});
			world.Add(effectEntity, new ResourceCollector { CollectorId = "stub_add_ten" });
			world.Add(effectEntity, new ForceResourceRecompute());

			var registry = new ResourceCollectorRegistry();
			registry.Register("stub_add_ten", new StubFixedDeltaCollector(10.0));

			// previousTime == currentTime: no day/month boundary at all — without the marker
			// this Daily effect would be skipped entirely (see daily_effect_not_applied_within_same_day).
			ResourceSystem.Update(world, Jan1, Jan1, registry, new[] { "test_resource" });

			Assert.Equal(110.0, world.Get<Resource>(re).Value);
			Assert.False(world.Has<ForceResourceRecompute>(effectEntity));

			// One-shot: a second same-tick call without re-adding the marker must not reapply.
			ResourceSystem.Update(world, Jan1, Jan1, registry, new[] { "test_resource" });
			Assert.Equal(110.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void resourceid_update_order_resolves_dependency_before_dependent() {
			var world = TestWorld.Create();
			int reA = world.Entity().Last;
			world.Add(reA, new ResourceOwner("Russia"));
			world.Add(reA, new Resource { ResourceId = "a", Value = 100.0 });

			int reB = world.Entity().Last;
			world.Add(reB, new ResourceOwner("Russia"));
			world.Add(reB, new Resource { ResourceId = "b", Value = 0.0 });

			int effectA = world.Entity().Last;
			world.Add(effectA, new ResourceOwner("Russia"));
			world.Add(effectA, new ResourceLink("a"));
			world.Add(effectA, new ResourceEffect { EffectId = "grow_a", Value = 0.0, PayType = PayType.Monthly });
			world.Add(effectA, new ResourceCollector { CollectorId = "add_fixed" });

			int effectB = world.Entity().Last;
			world.Add(effectB, new ResourceOwner("Russia"));
			world.Add(effectB, new ResourceLink("b"));
			world.Add(effectB, new ResourceEffect { EffectId = "mirror_a", Value = 0.0, PayType = PayType.Monthly });
			world.Add(effectB, new ResourceCollector { CollectorId = "mirror_a" });

			var registry = new ResourceCollectorRegistry();
			registry.Register("add_fixed", new StubFixedDeltaCollector(50.0));
			registry.Register("mirror_a", new MirrorResourceCollector("a"));

			ResourceSystem.Update(world, Jan31, Feb1, registry, new[] { "a", "b" });

			Assert.Equal(150.0, world.Get<Resource>(reA).Value);
			Assert.Equal(150.0, world.Get<Resource>(reB).Value);
		}

		[Fact]
		void resourceids_not_in_order_list_process_unaffected() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 5.0);

			var registry = new ResourceCollectorRegistry();
			ResourceSystem.Update(world, Jan31, Feb1, registry, new[] { "population" });

			Assert.Equal(105.0, world.Get<Resource>(re).Value);
		}

		[Fact]
		void null_registry_and_order_preserve_legacy_behavior() {
			var world = CreateWorldWithResource("Russia", "gold", 100.0, out int re);
			AddMonthlyEffect(world, "Russia", "gold", "income", 1.0);

			ResourceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(101.0, world.Get<Resource>(re).Value);
		}
	}
}
