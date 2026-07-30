using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class Wars {
		public static bool IsInWar(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (participants[i].CountryId == countryId) {
						return true;
					}
				}
			}
			return false;
		}

		public static bool IsWarFree(IReadOnlyWorld world, string countryId, string hqCountryId) {
			if (!string.IsNullOrEmpty(countryId) && IsInWar(world, countryId)) {
				return false;
			}
			if (!string.IsNullOrEmpty(hqCountryId) && IsInWar(world, hqCountryId)) {
				return false;
			}
			return true;
		}

		public static bool IsWarFree(
			IReadOnlyWorld world,
			string countryId,
			string orgId,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId) {
			string hqCountryId = "";
			if (hqCountryByOrgId != null && !string.IsNullOrEmpty(orgId)
				&& hqCountryByOrgId.TryGetValue(orgId, out string? resolved) && !string.IsNullOrEmpty(resolved)) {
				hqCountryId = resolved;
			}
			return IsWarFree(world, countryId, hqCountryId);
		}

		public static bool DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime) {
			return DeclareWar(world, attackerCountryId, defenderCountryId, currentTime, out _);
		}

		public static bool DeclareWar(
			World world,
			string attackerCountryId,
			string defenderCountryId,
			DateTime currentTime,
			out string? warId) {
			warId = null;
			if (attackerCountryId == defenderCountryId) {
				return false;
			}
			if (IsInWar(world, attackerCountryId) || IsInWar(world, defenderCountryId)) {
				return false;
			}

			warId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}";

			int warEntity = world.Create();
			world.Add(warEntity, new War { WarId = warId });
			world.Add(warEntity, new WarProgress { Value = 0 });

			int attackerEntity = world.Create();
			world.Add(attackerEntity, new WarParticipant {
				WarId = warId,
				Kind = WarParticipantKind.Attacker,
				CountryId = attackerCountryId
			});

			int defenderEntity = world.Create();
			world.Add(defenderEntity, new WarParticipant {
				WarId = warId,
				Kind = WarParticipantKind.Defender,
				CountryId = defenderCountryId
			});

			return true;
		}

		public static bool StopWar(World world, string countryId) {
			string? warId = null;
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].CountryId == countryId) {
						warId = participants[i].WarId;
						break;
					}
				}
				if (warId != null) { break; }
			}
			if (warId == null) {
				return false;
			}

			var matchingParticipants = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (participants[i].WarId == warId) {
						matchingParticipants.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in matchingParticipants) {
				world.Destroy(e);
			}

			int[] warRequired = { TypeId<War>.Value };
			var matchingWars = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = arch.GetColumn<War>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (wars[i].WarId == warId) {
						matchingWars.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in matchingWars) {
				world.Destroy(e);
			}

			return true;
		}
	}
}
