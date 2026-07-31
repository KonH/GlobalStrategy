using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

	namespace GS.Game.Systems {
	public static class WarBattles {
		public readonly struct ParticipantInfo {
			public readonly string CountryId;
			public readonly WarParticipantKind Side;

			public ParticipantInfo(string countryId, WarParticipantKind side) {
				CountryId = countryId;
				Side = side;
			}
		}

		public readonly struct BattleInfo {
			public readonly int Entity;
			public readonly Battle Value;

			public BattleInfo(int entity, Battle value) {
				Entity = entity;
				Value = value;
			}
		}

		public readonly struct ForceInfo {
			public readonly int Entity;
			public readonly BattleForce Value;

			public ForceInfo(int entity, BattleForce value) {
				Entity = entity;
				Value = value;
			}
		}

		public static List<ParticipantInfo> GetParticipants(IReadOnlyWorld world, string warId) {
			var result = new List<ParticipantInfo>();
			int[] required = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].WarId == warId) {
						result.Add(new ParticipantInfo(participants[i].CountryId, participants[i].Kind));
					}
				}
			}
			result.Sort((a, b) => {
				int side = a.Side.CompareTo(b.Side);
				return side != 0 ? side : string.CompareOrdinal(a.CountryId, b.CountryId);
			});
			return result;
		}

		public static List<BattleInfo> GetBattles(IReadOnlyWorld world, string warId, BattleState? state = null) {
			var result = new List<BattleInfo>();
			int[] required = { TypeId<Battle>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Battle[] battles = arch.GetColumn<Battle>();
				for (int i = 0; i < arch.Count; i++) {
					if (battles[i].WarId == warId && (!state.HasValue || battles[i].State == state.Value)) {
						result.Add(new BattleInfo(arch.Entities[i], battles[i]));
					}
				}
			}
			result.Sort((a, b) => {
				int sequence = a.Value.CreationSequence.CompareTo(b.Value.CreationSequence);
				if (sequence != 0) {
					return sequence;
				}
				int created = a.Value.CreatedAt.CompareTo(b.Value.CreatedAt);
				if (created != 0) {
					return created;
				}
				return string.CompareOrdinal(a.Value.BattleId, b.Value.BattleId);
			});
			return result;
		}

		public static List<ForceInfo> GetForces(IReadOnlyWorld world, string battleId) {
			var result = new List<ForceInfo>();
			int[] required = { TypeId<BattleForce>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				BattleForce[] forces = arch.GetColumn<BattleForce>();
				for (int i = 0; i < arch.Count; i++) {
					if (forces[i].BattleId == battleId) {
						result.Add(new ForceInfo(arch.Entities[i], forces[i]));
					}
				}
			}
			result.Sort((a, b) => {
				int side = a.Value.Side.CompareTo(b.Value.Side);
				return side != 0 ? side : string.CompareOrdinal(a.Value.CountryId, b.Value.CountryId);
			});
			return result;
		}

		// Finished battles are kept only for the war-progress-window's battle history display;
		// without a cap they accumulate for as long as the war stays unresolved (RoundIntervalHours
		// can be as low as 1 hour), eventually making every per-frame history rebuild and every
		// full-world Battle/BattleForce archetype scan slow enough to look like a hang.
		public static void PruneFinishedBattles(World world, string warId, int maxRetained) {
			List<BattleInfo> finished = GetBattles(world, warId, BattleState.Finished);
			int overflow = finished.Count - maxRetained;
			if (overflow <= 0) {
				return;
			}
			for (int i = 0; i < overflow; i++) {
				BattleInfo battle = finished[i];
				foreach (ForceInfo force in GetForces(world, battle.Value.BattleId)) {
					world.Destroy(force.Entity);
				}
				world.Destroy(battle.Entity);
			}
		}

		public static HashSet<string> GetActiveTargetIds(IReadOnlyWorld world, string warId) {
			var result = new HashSet<string>(StringComparer.Ordinal);
			foreach (BattleInfo battle in GetBattles(world, warId, BattleState.Active)) {
				result.Add(battle.Value.TargetProvinceId);
			}
			return result;
		}
	}
}
