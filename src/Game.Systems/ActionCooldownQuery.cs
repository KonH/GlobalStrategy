using System;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ActionCooldownQuery {
		public static TimeSpan? GetRemaining(IReadOnlyWorld world, string orgId, string actionId, DateTime currentTime) {
			int[] required = { TypeId<ActionCooldownState>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCooldownState[] states = arch.GetColumn<ActionCooldownState>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (states[i].OrgId == orgId && states[i].ActionId == actionId) {
						return states[i].EndTime > currentTime ? states[i].EndTime - currentTime : (TimeSpan?)null;
					}
				}
			}
			return null;
		}

		public static bool IsOnCooldown(IReadOnlyWorld world, string orgId, string actionId, DateTime currentTime) {
			return GetRemaining(world, orgId, actionId, currentTime).HasValue;
		}
	}
}
