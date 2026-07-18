using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ResourceSystem {
		public static void Update(
			World world, DateTime previousTime, DateTime currentTime,
			ResourceCollectorRegistry? collectorRegistry = null,
			IReadOnlyList<string>? resourceIdUpdateOrder = null) {

			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;

			if (collectorRegistry != null && resourceIdUpdateOrder != null && resourceIdUpdateOrder.Count > 0) {
				var ordered = new HashSet<string>(resourceIdUpdateOrder);
				foreach (string resourceId in resourceIdUpdateOrder) {
					ResolveCollectors(world, resourceId, isMonthBoundary, collectorRegistry);
					GatherAndApply(world, isMonthBoundary, linkedResourceId => linkedResourceId == resourceId);
				}
				GatherAndApply(world, isMonthBoundary, linkedResourceId => !ordered.Contains(linkedResourceId));
			} else {
				GatherAndApply(world, isMonthBoundary, null);
			}
		}

		static void ResolveCollectors(World world, string resourceId, bool isMonthBoundary, ResourceCollectorRegistry registry) {
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value,
				TypeId<ResourceCollector>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				ResourceCollector[] collectors = arch.GetColumn<ResourceCollector>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (links[i].ResourceId != resourceId) {
						continue;
					}
					var effect = effects[i];
					bool shouldApply = effect.PayType == PayType.Instant
						|| (effect.PayType == PayType.Monthly && isMonthBoundary);
					if (!shouldApply) {
						continue;
					}

					double currentValue = ResourceQuery.GetValue(world, owners[i].OwnerId, resourceId);
					var collector = registry.Resolve(collectors[i].CollectorId);
					effect.Value = collector.Compute(owners[i].OwnerId, currentValue, world);
					effects[i] = effect;
				}
			}
		}

		// NOTE: a ResourceCollector-tagged effect whose ResourceLink.ResourceId is not in
		// resourceIdUpdateOrder is never resolved — it applies its static (usually zero) Value
		// forever. Any new collector-driven resourceId must be added to resourceIdUpdateOrder.
		static void GatherAndApply(World world, bool isMonthBoundary, Func<string, bool>? resourceIdFilter) {
			int[] effectRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};

			var toApply = new List<(string OwnerId, string ResourceId, double Value, bool ClampToZero, int EffectEntity)>();
			var toDestroy = new List<int>();

			foreach (Archetype arch in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++) {
					if (resourceIdFilter != null && !resourceIdFilter(links[i].ResourceId)) {
						continue;
					}

					var effect = effects[i];
					bool shouldApply = effect.PayType == PayType.Instant
						|| (effect.PayType == PayType.Monthly && isMonthBoundary);
					if (!shouldApply) {
						continue;
					}

					double valueToApply = effect.Value;
					if (effect.MaxTotal > 0) {
						double remaining = effect.MaxTotal - Math.Abs(effect.AccumulatedTotal);
						if (remaining <= 0) {
							continue;
						}
						double absVal = Math.Abs(effect.Value);
						double clampedAbs = Math.Min(absVal, remaining);
						valueToApply = effect.Value < 0 ? -clampedAbs : clampedAbs;
						effect.AccumulatedTotal += Math.Abs(valueToApply);
						effects[i] = effect;
					}

					toApply.Add((owners[i].OwnerId, links[i].ResourceId, valueToApply, effect.ClampToZero, entities[i]));

					if (effect.PayType == PayType.Instant) {
						toDestroy.Add(entities[i]);
					}
				}
			}

			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};

			foreach ((string ownerId, string resourceId, double value, bool clampToZero, int effectEntity) in toApply) {
				foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId != ownerId || resources[i].ResourceId != resourceId) {
							continue;
						}
						if (clampToZero) {
							double current = resources[i].Value;
							double proposed = current + value;
							if ((current > 0 && proposed < 0) || (current < 0 && proposed > 0)) {
								resources[i].Value = 0;
								if (!toDestroy.Contains(effectEntity)) {
									toDestroy.Add(effectEntity);
								}
							} else {
								resources[i].Value += value;
							}
						} else {
							resources[i].Value += value;
						}
					}
				}
			}

			foreach (int e in toDestroy) {
				world.Destroy(e);
			}
		}
	}
}
