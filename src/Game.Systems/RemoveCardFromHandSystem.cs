using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class RemoveCardFromHandSystem {
		public static void Update(World world) {
			var toProcess = new List<int>();

			int[] succeededReq = { TypeId<CardUse>.Value, TypeId<ActionSucceeded>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(succeededReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcess.Add(arch.Entities[i]); }
			}

			int[] failedReq = { TypeId<CardUse>.Value, TypeId<ActionFailed>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(failedReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcess.Add(arch.Entities[i]); }
			}

			var toProcessOld = new List<int>();
			int[] succeededOldReq = { TypeId<CardUse>.Value, TypeId<ActionSucceeded>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(succeededOldReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcessOld.Add(arch.Entities[i]); }
			}
			int[] failedOldReq = { TypeId<CardUse>.Value, TypeId<ActionFailed>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(failedOldReq, null)) {
				for (int i = 0; i < arch.Count; i++) { toProcessOld.Add(arch.Entities[i]); }
			}

			foreach (int e in toProcess) {
				world.Remove<CardInHand>(e);
				world.Add(e, new CardDiscard());
			}
			foreach (int e in toProcessOld) {
				world.Remove<InHand>(e);
				world.Add(e, new CardDiscard());
			}
		}
	}
}
