using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
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
			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) => _delta;
		}

		sealed class MirrorResourceCollector : IResourceCollector {
			readonly string _sourceResourceId;
			public MirrorResourceCollector(string sourceResourceId) { _sourceResourceId = sourceResourceId; }

			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
				int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId == ownerId && resources[i].ResourceId == _sourceResourceId) {
							return resources[i].Value - currentValue;
						}
					}
				}
				return -currentValue;
			}
		}

		static World CreateWorldWithResource(string countryId, string resourceId, double initialValue,
			out int resourceEntity) {
			var world = new World();
			resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(countryId));
			world.Add(resourceEntity, new Resource { ResourceId = resourceId, Value = initialValue });
			return world;
		}

		static int AddMonthlyEffect(World world, string countryId, string resourceId,
			string effectId, double value) {
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner(countryId));
			world.Add(effectEntity, new ResourceLink(resourceId));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = effectId,
				Value = value,
				PayType = PayType.Monthly
			});
			return effectEntity;
		}

		static int AddInstantEffect(World world, string countryId, string resourceId,
			string effectId, double value) {
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner(countryId));
			world.Add(effectEntity, new ResourceLink(resourceId));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = effectId,
				Value = value,
				PayType = PayType.Instant
			});
			return effectEntity;
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
			var world = new World();
			int re1 = world.Create();
			world.Add(re1, new ResourceOwner("Russia"));
			world.Add(re1, new Resource { ResourceId = "gold", Value = 100.0 });

			int re2 = world.Create();
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
			int effectEntity = world.Create();
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
		void resourceid_update_order_resolves_dependency_before_dependent() {
			var world = new World();
			int reA = world.Create();
			world.Add(reA, new ResourceOwner("Russia"));
			world.Add(reA, new Resource { ResourceId = "a", Value = 100.0 });

			int reB = world.Create();
			world.Add(reB, new ResourceOwner("Russia"));
			world.Add(reB, new Resource { ResourceId = "b", Value = 0.0 });

			int effectA = world.Create();
			world.Add(effectA, new ResourceOwner("Russia"));
			world.Add(effectA, new ResourceLink("a"));
			world.Add(effectA, new ResourceEffect { EffectId = "grow_a", Value = 0.0, PayType = PayType.Monthly });
			world.Add(effectA, new ResourceCollector { CollectorId = "add_fixed" });

			int effectB = world.Create();
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
