using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ControlSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1  = new DateTime(1880, 2,  1,  0, 0, 0);
		static readonly DateTime Jan1  = new DateTime(1880, 1,  1,  0, 0, 0);
		static readonly DateTime Jan2  = new DateTime(1880, 1,  2,  0, 0, 0);

		static void AddResource(World world, ResourceQuery resources, string ownerId, string resourceId, double value) {
			resources.Set(world, ownerId, resourceId, value);
		}

		static double GetResource(World world, ResourceQuery resources, string ownerId, string resourceId) {
			return resources.GetValue(world, ownerId, resourceId);
		}

		static void AddMonthlyEffect(World world, string ownerId, string resourceId, double value) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(ownerId));
			world.Add(e, new ResourceLink(resourceId));
			world.Add(e, new ResourceEffect { EffectId = "income", Value = value, PayType = PayType.Monthly });
		}

		static int AddControl(World world, string orgId, string countryId, int value, string? effectId = null) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId     = orgId,
				CountryId = countryId,
				Value     = value,
				EffectId  = effectId ?? $"base_{orgId}"
			});
			return e;
		}

		// Test 1: base control entity has Value == 10 when created with the expected data
		[Fact]
		void base_entity_has_expected_value() {
			var world = new World();
			int e = AddControl(world, "Org1", "Russia", 10, "base_Org1");
			int[] required = { TypeId<ControlEffect>.Value };
			int found = 0;
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].CountryId == "Russia" && effects[i].EffectId == "base_Org1") {
						Assert.Equal(10, effects[i].Value);
						found++;
					}
				}
			}
			Assert.Equal(1, found);
		}

		// Test 2: gold transfer at month boundary
		[Fact]
		void gold_transferred_at_month_boundary() {
			var world = new World();
			var resources = new ResourceQuery();
			AddResource(world, resources, "Russia", "gold", 500.0);
			AddResource(world, resources, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddControl(world, "Org1", "Russia", 20);

			ControlSystem.Update(world, Jan31, Feb1, resources: resources);

			Assert.Equal(300.0, GetResource(world, resources, "Russia", "gold"), 2);
			Assert.Equal(200.0, GetResource(world, resources, "Org1", "gold"), 2);
		}

		// Test 3: zero control — org receives nothing
		[Fact]
		void zero_control_receives_nothing() {
			var world = new World();
			var resources = new ResourceQuery();
			AddResource(world, resources, "Russia", "gold", 500.0);
			AddResource(world, resources, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddControl(world, "Org1", "Russia", 0);

			ControlSystem.Update(world, Jan31, Feb1, resources: resources);

			Assert.Equal(500.0, GetResource(world, resources, "Russia", "gold"));
			Assert.Equal(0.0, GetResource(world, resources, "Org1", "gold"));
		}

		// Test 3b: no month boundary — nothing applied
		[Fact]
		void no_transfer_within_same_month() {
			var world = new World();
			var resources = new ResourceQuery();
			AddResource(world, resources, "Russia", "gold", 500.0);
			AddResource(world, resources, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddControl(world, "Org1", "Russia", 20);

			ControlSystem.Update(world, Jan1, Jan2, resources: resources);

			Assert.Equal(500.0, GetResource(world, resources, "Russia", "gold"));
			Assert.Equal(0.0, GetResource(world, resources, "Org1", "gold"));
		}

		// Test 4: multiple orgs receive proportional amounts
		[Fact]
		void multiple_orgs_receive_correct_amounts() {
			var world = new World();
			var resources = new ResourceQuery();
			AddResource(world, resources, "Russia", "gold", 500.0);
			AddResource(world, resources, "Org1",   "gold", 0.0);
			AddResource(world, resources, "Org2",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddControl(world, "Org1", "Russia", 20);
			AddControl(world, "Org2", "Russia", 30);

			ControlSystem.Update(world, Jan31, Feb1, resources: resources);

			Assert.Equal(200.0, GetResource(world, resources, "Org1", "gold"), 2);
			Assert.Equal(300.0, GetResource(world, resources, "Org2", "gold"), 2);
			Assert.Equal(0.0, GetResource(world, resources, "Russia", "gold"), 2);
		}

		// Test 5: ApplyChangeControl creates/updates permanent entity
		[Fact]
		void apply_change_control_creates_permanent_entity() {
			var world = new World();
			AddControl(world, "Org1", "Russia", 10, "base_Org1");

			ControlSystem.ApplyChangeControl(world, "Org1", "Russia", 5, 100);

			int permanentCount = 0;
			int permanentValue = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].EffectId == "permanent_Org1_Russia") {
						permanentCount++;
						permanentValue = effects[i].Value;
					}
				}
			}
			Assert.Equal(1, permanentCount);
			Assert.Equal(5, permanentValue);
		}

		// Test 6: pool cap — cannot exceed 100 - otherOrgsControl
		[Fact]
		void apply_change_control_clamped_at_pool_cap() {
			var world = new World();
			AddControl(world, "Org2", "Russia", 80, "base_Org2"); // other org holds 80

			ControlSystem.ApplyChangeControl(world, "Org1", "Russia", 30, 100); // can only get 20

			int permanentValue = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].EffectId == "permanent_Org1_Russia") {
						permanentValue = effects[i].Value;
					}
				}
			}
			Assert.Equal(20, permanentValue);
		}

		// Test 7: cannot go below 0
		[Fact]
		void apply_change_control_clamped_at_zero() {
			var world = new World();
			int permanentEntity = world.Create();
			world.Add(permanentEntity, new ControlEffect {
				OrgId = "Org1", CountryId = "Russia", Value = 5, EffectId = "permanent_Org1_Russia"
			});

			ControlSystem.ApplyChangeControl(world, "Org1", "Russia", -20, 100);

			Assert.False(world.IsAlive(permanentEntity));
		}

		// Test 9: two orgs in the same country split monthly income proportionally (multi-org regression)
		[Fact]
		void two_orgs_in_same_country_split_monthly_income_proportionally() {
			var world = new World();
			var resources = new ResourceQuery();
			AddResource(world, resources, "Russia", "gold", 500.0);
			AddResource(world, resources, "OrgA", "gold", 0.0);
			AddResource(world, resources, "OrgB", "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddControl(world, "OrgA", "Russia", 30, "base_OrgA");
			AddControl(world, "OrgB", "Russia", 20, "base_OrgB");

			ControlSystem.Update(world, Jan31, Feb1, resources: resources);

			Assert.Equal(300.0, GetResource(world, resources, "OrgA", "gold"), 2);
			Assert.Equal(200.0, GetResource(world, resources, "OrgB", "gold"), 2);
			Assert.Equal(0.0, GetResource(world, resources, "Russia", "gold"), 2);
		}

		// Test 8: base effect entity remains untouched after command
		[Fact]
		void base_entity_untouched_after_change_control_command() {
			var world = new World();
			int baseEntity = AddControl(world, "Org1", "Russia", 10, "base_Org1");

			ControlSystem.ApplyChangeControl(world, "Org1", "Russia", 5, 100);

			Assert.True(world.IsAlive(baseEntity));
			Assert.Equal(10, world.Get<ControlEffect>(baseEntity).Value);
		}
	}
}
