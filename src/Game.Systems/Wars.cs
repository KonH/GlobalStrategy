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

		public static bool DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime) {
			if (attackerCountryId == defenderCountryId) {
				return false;
			}
			if (IsInWar(world, attackerCountryId) || IsInWar(world, defenderCountryId)) {
				return false;
			}

			string warId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}";

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
			var matchingParticipants = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (participants[i].CountryId == countryId) {
						warId = participants[i].WarId;
					}
				}
			}
			if (warId == null) {
				return false;
			}

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
