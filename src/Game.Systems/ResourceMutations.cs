using System;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	/// <summary>
	/// Thin adapters over <see cref="ResourceQuery"/> for call sites that already hold a query instance.
	/// Prefer calling <see cref="ResourceQuery"/> methods directly in new code.
	/// </summary>
	public static class ResourceMutations {
		public static bool TrySetValue(
			ResourceQuery resources,
			World world, string ownerId, string resourceId, double value, out double oldValue) {
			return resources.TrySetValue(world, ownerId, resourceId, value, out oldValue);
		}

		public static bool TryApplyClampedDelta(
			ResourceQuery resources,
			World world, string ownerId, string resourceId, double delta,
			double minimum, double maximum, out double appliedDelta) {
			return resources.TryApplyClampedDelta(
				world, ownerId, resourceId, delta, minimum, maximum, out appliedDelta);
		}

		public static bool TryApplyClampedDelta(
			ResourceQuery resources,
			World world, string ownerId, string resourceId, double delta,
			ResourceDefinition? definition, string effectId, DateTime timestamp,
			double defaultMinimum, double defaultMaximum, out double appliedDelta) {
			return resources.TryApplyClampedDelta(
				world, ownerId, resourceId, delta, definition, effectId, timestamp,
				defaultMinimum, defaultMaximum, out appliedDelta);
		}

		public static void TryAppendHistory(
			World world, int resourceEntity, ResourceDefinition? definition,
			string effectId, double appliedDelta, DateTime timestamp) {
			ResourceQuery.TryAppendHistory(
				world, resourceEntity, definition, effectId, appliedDelta, timestamp);
		}
	}
}
