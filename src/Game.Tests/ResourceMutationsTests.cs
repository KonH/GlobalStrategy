using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceMutationsTests {
		static ResourceConfig HistoryConfig() => new ResourceConfig {
			Resources = new List<ResourceDefinition> {
				new ResourceDefinition {
					ResourceId = ResourceDefinitions.WarProgress,
					RecordHistory = true,
					MinValue = -100,
					MaxValue = 100
				}
			}
		};

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

		[Fact]
		void record_history_appends_non_zero_applied_delta() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("war_1", OwnerType.War));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.WarProgress, Value = 0 });
			var timestamp = new DateTime(1880, 2, 1);
			ResourceDefinition definition = HistoryConfig().FindResource(ResourceDefinitions.WarProgress)!;

			Assert.True(ResourceMutations.TryApplyClampedDelta(
				world, "war_1", ResourceDefinitions.WarProgress, 5, definition,
				"test_effect", timestamp, double.MinValue, double.MaxValue, out double applied));
			Assert.Equal(5, applied);

			ref ResourceHistory history = ref world.Get<ResourceHistory>(entity);
			Assert.Single(history.History);
			Assert.Equal("test_effect", history.History[0].EffectId);
			Assert.Equal(5, history.History[0].AppliedDelta);
			Assert.Equal(timestamp, history.History[0].Timestamp);
		}

		[Fact]
		void record_history_off_does_not_append() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("war_1", OwnerType.War));
			world.Add(entity, new Resource { ResourceId = "recruits", Value = 10 });
			var definition = new ResourceDefinition { ResourceId = "recruits", RecordHistory = false };

			Assert.True(ResourceMutations.TryApplyClampedDelta(
				world, "war_1", "recruits", 3, definition,
				"ignored", DateTime.UtcNow, 0, double.MaxValue, out _));

			Assert.False(world.Has<ResourceHistory>(entity));
		}

		[Fact]
		void zero_applied_delta_does_not_append_history() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("war_1", OwnerType.War));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.WarProgress, Value = 100 });
			ResourceDefinition definition = HistoryConfig().FindResource(ResourceDefinitions.WarProgress)!;

			Assert.True(ResourceMutations.TryApplyClampedDelta(
				world, "war_1", ResourceDefinitions.WarProgress, 5, definition,
				"clamped", DateTime.UtcNow, double.MinValue, double.MaxValue, out double applied));
			Assert.Equal(0, applied);
			Assert.False(world.Has<ResourceHistory>(entity));
		}

		[Fact]
		void clamp_stores_actual_applied_amount_in_history() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new ResourceOwner("war_1", OwnerType.War));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.WarProgress, Value = 95 });
			ResourceDefinition definition = HistoryConfig().FindResource(ResourceDefinitions.WarProgress)!;

			Assert.True(ResourceMutations.TryApplyClampedDelta(
				world, "war_1", ResourceDefinitions.WarProgress, 10, definition,
				"battle", DateTime.UtcNow, double.MinValue, double.MaxValue, out double applied));
			Assert.Equal(5, applied);
			Assert.Equal(100, ResourceQuery.GetValue(world, "war_1", ResourceDefinitions.WarProgress));

			ref ResourceHistory history = ref world.Get<ResourceHistory>(entity);
			Assert.Single(history.History);
			Assert.Equal(5, history.History[0].AppliedDelta);
		}
	}
}
