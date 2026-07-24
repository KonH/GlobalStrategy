using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class SetCountryRelationSystem {
		public static void Update(World world, int proximityEntity, Random rng) {
			int[] required = { TypeId<SetCountryRelationEffect>.Value };
			var toProcess = new List<(int entity, string orgId, string countryId, RelationKind kind)>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				SetCountryRelationEffect[] effects = arch.GetColumn<SetCountryRelationEffect>();
				for (int i = 0; i < arch.Count; i++) {
					toProcess.Add((arch.Entities[i], effects[i].OrgId, effects[i].CountryId, effects[i].Kind));
				}
			}
			if (toProcess.Count == 0) { return; }

			ProximityMapData pm = default;
			bool hasPm = proximityEntity >= 0;
			if (hasPm) { pm = world.Get<ProximityMapData>(proximityEntity); }

			foreach (var (entity, orgId, countryId, kind) in toProcess) {
				ResolveRelation(world, rng, orgId, countryId, kind, hasPm, pm);
				world.Destroy(entity);
			}
		}

		static void ResolveRelation(World world, Random rng, string orgId, string countryId, RelationKind kind, bool hasPm, ProximityMapData pm) {
			var candidates = CountryRelations.GetSuitableRelationCandidates(world, countryId);
			if (candidates.Count == 0) { return; }

			string chosen;
			if (rng.NextDouble() < 0.5) {
				chosen = PickByProximity(rng, countryId, candidates, hasPm, pm);
			} else {
				chosen = candidates[rng.Next(candidates.Count)];
			}

			CountryRelations.SetRelation(world, countryId, chosen, kind);

			// Game Log event — separate sibling entity, not attached to SetCountryRelationEffect.
			// See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			int ge = world.Create();
			world.Add(ge, new RelationSetApplied { OrgId = orgId, CountryId = countryId, TargetCountryId = chosen, Kind = kind });
		}

		static string PickByProximity(Random rng, string anchorCountryId, List<string> candidates, bool hasPm, ProximityMapData pm) {
			float totalWeight = 0f;
			var weights = new float[candidates.Count];
			for (int i = 0; i < candidates.Count; i++) {
				string b = candidates[i];
				float w = 1f;
				if (hasPm && pm.Distances != null) {
					string a = anchorCountryId;
					string ka = string.CompareOrdinal(a, b) <= 0 ? a : b;
					string kb = string.CompareOrdinal(a, b) <= 0 ? b : a;
					if (pm.Distances.TryGetValue((ka, kb), out float d)) {
						w = d < 0.0001f ? 1e6f : 1f / d;
					}
				}
				weights[i] = w;
				totalWeight += w;
			}

			float minChance = 0.01f;
			float floorWeight = minChance * totalWeight;
			for (int i = 0; i < weights.Length; i++) {
				if (weights[i] < floorWeight) { weights[i] = floorWeight; }
			}

			float wTotal = 0f;
			for (int i = 0; i < weights.Length; i++) { wTotal += weights[i]; }
			float pick = (float)rng.NextDouble() * wTotal;
			float acc = 0f;
			int chosenIndex = 0;
			for (int i = 0; i < weights.Length; i++) {
				acc += weights[i];
				if (pick <= acc) { chosenIndex = i; break; }
			}
			return candidates[chosenIndex];
		}
	}
}
