using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ClearCountryRelationSystem {
		public static void Update(World world) {
			int[] required = { TypeId<ClearCountryRelationEffect>.Value };
			var toProcess = new List<(string orgId, string countryId, string targetCountryId)>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ClearCountryRelationEffect[] effects = arch.GetColumn<ClearCountryRelationEffect>();
				for (int i = 0; i < arch.Count; i++) {
					toProcess.Add((effects[i].OrgId, effects[i].CountryId, effects[i].TargetCountryId));
				}
			}
			if (toProcess.Count == 0) { return; }

			foreach (var (orgId, countryId, targetCountryId) in toProcess) {
				var kind = CountryRelations.GetRelation(world, countryId, targetCountryId);
				CountryRelations.RemoveRelation(world, countryId, targetCountryId);

				// Game Log event — separate sibling entity, not attached to
				// ClearCountryRelationEffect. See
				// Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
				int ge = world.Create();
				world.Add(ge, new RelationClearedApplied {
					OrgId = orgId,
					CountryId = countryId,
					TargetCountryId = targetCountryId,
					Kind = kind ?? default
				});
			}

			// Marker entities are deliberately not destroyed here — CleanupActionEffectsSystem
			// removes ClearCountryRelationEffect on the next tick, mirroring DiscoverCountrySystem's
			// treatment of DiscoverCountryEffect. See ecs_patterns.md's "no system-to-system calls"
			// rule and the ordering note above.
		}
	}
}
