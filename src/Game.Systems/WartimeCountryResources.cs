using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class WartimeCountryResources {
		public static void CreateForCountry(World world, string countryId) {
			CreateResourcePair(world, countryId, ResourceDefinitions.Damage, DamageCollector.Id);
			CreateResourcePair(world, countryId, ResourceDefinitions.Durability, DurabilityCollector.Id);
		}

		public static void DestroyForCountry(World world, string countryId) {
			var toDestroy = new List<int>();

			int[] resourceRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId || owners[i].OwnerType != OwnerType.Country) {
						continue;
					}
					if (!IsWartimeResourceId(resources[i].ResourceId)) {
						continue;
					}
					toDestroy.Add(arch.Entities[i]);
				}
			}

			int[] effectRequired = { TypeId<ResourceOwner>.Value, TypeId<ResourceLink>.Value, TypeId<ResourceEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId || owners[i].OwnerType != OwnerType.Country) {
						continue;
					}
					if (!IsWartimeResourceId(links[i].ResourceId)) {
						continue;
					}
					toDestroy.Add(arch.Entities[i]);
				}
			}

			foreach (int entity in toDestroy) {
				world.Destroy(entity);
			}
		}

		static void CreateResourcePair(World world, string countryId, string resourceId, string collectorId) {
			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(resourceEntity, new Resource { ResourceId = resourceId, Value = 0 });

			int instantEffectEntity = world.Create();
			world.Add(instantEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(instantEffectEntity, new ResourceLink(resourceId));
			world.Add(instantEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_seed_{countryId}",
				PayType = PayType.Instant
			});
			world.Add(instantEffectEntity, new ResourceCollector { CollectorId = collectorId });

			int dailyEffectEntity = world.Create();
			world.Add(dailyEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(dailyEffectEntity, new ResourceLink(resourceId));
			world.Add(dailyEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_daily_{countryId}",
				PayType = PayType.Daily
			});
			world.Add(dailyEffectEntity, new ResourceCollector { CollectorId = collectorId });
		}

		static bool IsWartimeResourceId(string resourceId) {
			return resourceId == ResourceDefinitions.Damage || resourceId == ResourceDefinitions.Durability;
		}
	}
}
