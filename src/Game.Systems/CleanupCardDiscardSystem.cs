using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CleanupCardDiscardSystem {
		public static void Update(World world) {
			int[] required = { TypeId<CardDiscard>.Value };
			var entities = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					entities.Add(arch.Entities[i]);
				}
			}
			foreach (int e in entities) {
				world.Remove<CardDiscard>(e);
			}
		}
	}
}
