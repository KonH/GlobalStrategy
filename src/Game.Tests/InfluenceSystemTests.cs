using System;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class InfluenceSystemTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1  = new DateTime(1880, 2,  1,  0, 0, 0);
		static readonly DateTime Jan1  = new DateTime(1880, 1,  1,  0, 0, 0);
		static readonly DateTime Jan2  = new DateTime(1880, 1,  2,  0, 0, 0);

		static int AddResource(World world, string ownerId, string resourceId, double value) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(ownerId));
			world.Add(e, new Resource { ResourceId = resourceId, Value = value });
			return e;
		}

		static void AddMonthlyEffect(World world, string ownerId, string resourceId, double value) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(ownerId));
			world.Add(e, new ResourceLink(resourceId));
			world.Add(e, new ResourceEffect { EffectId = "income", Value = value, PayType = PayType.Monthly });
		}

		static int AddInfluence(World world, string orgId, string countryId, int value, string? effectId = null) {
			int e = world.Create();
			world.Add(e, new InfluenceEffect {
				OrgId     = orgId,
				CountryId = countryId,
				Value     = value,
				EffectId  = effectId ?? $"base_{orgId}"
			});
			return e;
		}

		// Test 1: base influence entity has Value == 10 when created with the expected data
		[Fact]
		void base_entity_has_expected_value() {
			var world = new World();
			int e = AddInfluence(world, "Org1", "Russia", 10, "base_Org1");
			int[] required = { TypeId<InfluenceEffect>.Value };
			int found = 0;
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<InfluenceEffect>();
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
			int countryGold = AddResource(world, "Russia", "gold", 500.0);
			int orgGold     = AddResource(world, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddInfluence(world, "Org1", "Russia", 20);

			InfluenceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(300.0, world.Get<Resource>(countryGold).Value, 2);
			Assert.Equal(200.0, world.Get<Resource>(orgGold).Value, 2);
		}

		// Test 3: zero influence — org receives nothing
		[Fact]
		void zero_influence_receives_nothing() {
			var world = new World();
			int countryGold = AddResource(world, "Russia", "gold", 500.0);
			int orgGold     = AddResource(world, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddInfluence(world, "Org1", "Russia", 0);

			InfluenceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(500.0, world.Get<Resource>(countryGold).Value);
			Assert.Equal(0.0, world.Get<Resource>(orgGold).Value);
		}

		// Test 3b: no month boundary — nothing applied
		[Fact]
		void no_transfer_within_same_month() {
			var world = new World();
			int countryGold = AddResource(world, "Russia", "gold", 500.0);
			int orgGold     = AddResource(world, "Org1",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddInfluence(world, "Org1", "Russia", 20);

			InfluenceSystem.Update(world, Jan1, Jan2);

			Assert.Equal(500.0, world.Get<Resource>(countryGold).Value);
			Assert.Equal(0.0, world.Get<Resource>(orgGold).Value);
		}

		// Test 4: multiple orgs receive proportional amounts
		[Fact]
		void multiple_orgs_receive_correct_amounts() {
			var world = new World();
			int countryGold = AddResource(world, "Russia", "gold", 500.0);
			int org1Gold    = AddResource(world, "Org1",   "gold", 0.0);
			int org2Gold    = AddResource(world, "Org2",   "gold", 0.0);
			AddMonthlyEffect(world, "Russia", "gold", 1000.0);
			AddInfluence(world, "Org1", "Russia", 20);
			AddInfluence(world, "Org2", "Russia", 30);

			InfluenceSystem.Update(world, Jan31, Feb1);

			Assert.Equal(200.0, world.Get<Resource>(org1Gold).Value, 2);
			Assert.Equal(300.0, world.Get<Resource>(org2Gold).Value, 2);
			Assert.Equal(0.0, world.Get<Resource>(countryGold).Value, 2);
		}

		// Test 5: ApplyChangeInfluence creates/updates permanent entity
		[Fact]
		void apply_change_influence_creates_permanent_entity() {
			var world = new World();
			AddInfluence(world, "Org1", "Russia", 10, "base_Org1");

			InfluenceSystem.ApplyChangeInfluence(world, "Org1", "Russia", 5);

			int permanentCount = 0;
			int permanentValue = 0;
			int[] required = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<InfluenceEffect>();
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

		// Test 6: pool cap — cannot exceed 100 - otherOrgsInfluence
		[Fact]
		void apply_change_influence_clamped_at_pool_cap() {
			var world = new World();
			AddInfluence(world, "Org2", "Russia", 80, "base_Org2"); // other org holds 80

			InfluenceSystem.ApplyChangeInfluence(world, "Org1", "Russia", 30); // can only get 20

			int permanentValue = 0;
			int[] required = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var effects = arch.GetColumn<InfluenceEffect>();
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
		void apply_change_influence_clamped_at_zero() {
			var world = new World();
			int permanentEntity = world.Create();
			world.Add(permanentEntity, new InfluenceEffect {
				OrgId = "Org1", CountryId = "Russia", Value = 5, EffectId = "permanent_Org1_Russia"
			});

			InfluenceSystem.ApplyChangeInfluence(world, "Org1", "Russia", -20);

			Assert.False(world.IsAlive(permanentEntity));
		}

		// Test 8: base effect entity remains untouched after command
		[Fact]
		void base_entity_untouched_after_change_influence_command() {
			var world = new World();
			int baseEntity = AddInfluence(world, "Org1", "Russia", 10, "base_Org1");

			InfluenceSystem.ApplyChangeInfluence(world, "Org1", "Russia", 5);

			Assert.True(world.IsAlive(baseEntity));
			Assert.Equal(10, world.Get<InfluenceEffect>(baseEntity).Value);
		}
	}
}
