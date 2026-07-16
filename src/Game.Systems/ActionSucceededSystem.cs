using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ActionSucceededSystem {
		public static void Update(World world, ActionConfig config) {
			int[] validRequired = { TypeId<GameAction>.Value, TypeId<ActionValid>.Value, TypeId<CardUse>.Value };
			var succeeded = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(validRequired, null)) {
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					succeeded.Add(arch.Entities[i]);
				}
			}
			foreach (int e in succeeded) {
				world.Add(e, new ActionSucceeded());
			}

			int[] invalidRequired = { TypeId<GameAction>.Value, TypeId<CardUse>.Value };
			int[] excludeValid = { TypeId<ActionValid>.Value };
			var failed = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(invalidRequired, excludeValid)) {
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					failed.Add(arch.Entities[i]);
				}
			}
			foreach (int e in failed) {
				world.Add(e, new ActionFailed());
			}
		}
	}
}
