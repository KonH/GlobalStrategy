using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class SetCountryRelationSystem {
		public static void Update(World world, CountryRelations relations) {
			int[] required = { TypeId<SetCountryRelationEffect>.Value };
			var toProcess = new List<(int entity, string orgId, string countryId, string targetCountryId, RelationKind kind)>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				SetCountryRelationEffect[] effects = arch.GetColumn<SetCountryRelationEffect>();
				for (int i = 0; i < arch.Count; i++) {
					toProcess.Add((arch.Entities[i], effects[i].OrgId, effects[i].CountryId, effects[i].TargetCountryId, effects[i].Kind));
				}
			}
			if (toProcess.Count == 0) { return; }

			foreach (var (entity, orgId, countryId, targetCountryId, kind) in toProcess) {
				relations.SetRelation(world, countryId, targetCountryId, kind);

				// Game Log event — separate sibling entity, not attached to SetCountryRelationEffect.
				// See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
				int ge = world.Create();
				world.Add(ge, new RelationSetApplied { OrgId = orgId, CountryId = countryId, TargetCountryId = targetCountryId, Kind = kind });

				world.Destroy(entity);
			}
		}
	}
}
