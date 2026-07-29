using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WartimeResourcesTests {
		static readonly DateTime DeclareTime = new DateTime(1880, 1, 1);

		record ResourceEntry(string OwnerId, OwnerType OwnerType, string ResourceId, double Value);
		record CollectorEntry(string OwnerId, OwnerType OwnerType, string ResourceId, PayType PayType, string CollectorId);

		static List<ResourceEntry> GetResources(World world) {
			var result = new List<ResourceEntry>();
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add(new ResourceEntry(owners[i].OwnerId, owners[i].OwnerType, resources[i].ResourceId, resources[i].Value));
				}
			}
			return result;
		}

		static List<CollectorEntry> GetCollectors(World world) {
			var result = new List<CollectorEntry>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value,
				TypeId<ResourceCollector>.Value
			};
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				ResourceLink[] links = archetype.GetColumn<ResourceLink>();
				ResourceEffect[] effects = archetype.GetColumn<ResourceEffect>();
				ResourceCollector[] collectors = archetype.GetColumn<ResourceCollector>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add(new CollectorEntry(
						owners[i].OwnerId, owners[i].OwnerType, links[i].ResourceId,
						effects[i].PayType, collectors[i].CollectorId));
				}
			}
			return result;
		}

		static List<ResourceEntry> WartimeResources(IEnumerable<ResourceEntry> resources) {
			return resources.Where(r =>
				r.OwnerType == OwnerType.Country &&
				(r.ResourceId == ResourceDefinitions.Damage || r.ResourceId == ResourceDefinitions.Durability)).ToList();
		}

		static List<CollectorEntry> WartimeCollectors(IEnumerable<CollectorEntry> collectors) {
			return collectors.Where(c =>
				c.OwnerType == OwnerType.Country &&
				(c.ResourceId == ResourceDefinitions.Damage || c.ResourceId == ResourceDefinitions.Durability)).ToList();
		}

		static void AssertCountryHasWartimePair(IEnumerable<ResourceEntry> resources, IEnumerable<CollectorEntry> collectors, string countryId) {
			Assert.Single(resources, r =>
				r.OwnerId == countryId && r.OwnerType == OwnerType.Country &&
				r.ResourceId == ResourceDefinitions.Damage && r.Value == 0);
			Assert.Single(resources, r =>
				r.OwnerId == countryId && r.OwnerType == OwnerType.Country &&
				r.ResourceId == ResourceDefinitions.Durability && r.Value == 0);

			Assert.Single(collectors, c =>
				c.OwnerId == countryId && c.OwnerType == OwnerType.Country &&
				c.ResourceId == ResourceDefinitions.Damage && c.PayType == PayType.Instant &&
				c.CollectorId == DamageCollector.Id);
			Assert.Single(collectors, c =>
				c.OwnerId == countryId && c.OwnerType == OwnerType.Country &&
				c.ResourceId == ResourceDefinitions.Damage && c.PayType == PayType.Daily &&
				c.CollectorId == DamageCollector.Id);
			Assert.Single(collectors, c =>
				c.OwnerId == countryId && c.OwnerType == OwnerType.Country &&
				c.ResourceId == ResourceDefinitions.Durability && c.PayType == PayType.Instant &&
				c.CollectorId == DurabilityCollector.Id);
			Assert.Single(collectors, c =>
				c.OwnerId == countryId && c.OwnerType == OwnerType.Country &&
				c.ResourceId == ResourceDefinitions.Durability && c.PayType == PayType.Daily &&
				c.CollectorId == DurabilityCollector.Id);
		}

		[Fact]
		void declare_war_creates_damage_and_durability_resources_and_effects_for_both_countries() {
			var world = new World();

			Assert.True(Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime));

			List<ResourceEntry> resources = GetResources(world);
			List<CollectorEntry> collectors = GetCollectors(world);
			Assert.Equal(4, WartimeResources(resources).Count);
			Assert.Equal(8, WartimeCollectors(collectors).Count);
			AssertCountryHasWartimePair(resources, collectors, "Great_Britain");
			AssertCountryHasWartimePair(resources, collectors, "France");
		}

		[Fact]
		void stop_war_destroys_damage_and_durability_for_both_former_participants() {
			var world = new World();
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			Assert.True(Wars.StopWar(world, "Great_Britain"));

			Assert.Empty(WartimeResources(GetResources(world)));
			Assert.Empty(WartimeCollectors(GetCollectors(world)));
			Assert.Equal(0, ResourceQuery.GetValue(world, "Great_Britain", ResourceDefinitions.Damage));
			Assert.Equal(0, ResourceQuery.GetValue(world, "Great_Britain", ResourceDefinitions.Durability));
			Assert.Equal(0, ResourceQuery.GetValue(world, "France", ResourceDefinitions.Damage));
			Assert.Equal(0, ResourceQuery.GetValue(world, "France", ResourceDefinitions.Durability));
		}

		[Fact]
		void declare_war_no_op_does_not_duplicate_wartime_resources() {
			var world = new World();
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			int resourceCount = WartimeResources(GetResources(world)).Count;
			int collectorCount = WartimeCollectors(GetCollectors(world)).Count;

			Assert.False(Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime.AddDays(1)));

			Assert.Equal(resourceCount, WartimeResources(GetResources(world)).Count);
			Assert.Equal(collectorCount, WartimeCollectors(GetCollectors(world)).Count);
			Assert.Equal(4, resourceCount);
			Assert.Equal(8, collectorCount);
		}
	}
}
