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
			if (!TryGetWarId(world, countryId, out string warId, out _)) {
				return false;
			}
			DestroyWarAndParticipants(world, warId);
			return true;
		}

		public static bool ResolveWar(World world, string countryId, WarOutcome outcomeForCountry) {
			if (!TryGetWarId(world, countryId, out string warId, out _)) {
				return false;
			}
			string? opponentCountryId = FindOpponentCountryId(world, warId, countryId);
			if (opponentCountryId == null) {
				return false;
			}
			string winnerCountryId = outcomeForCountry == WarOutcome.Win ? countryId : opponentCountryId;
			string loserCountryId = outcomeForCountry == WarOutcome.Win ? opponentCountryId : countryId;
			DestroyWarAndParticipants(world, warId);

			int appliedEntity = world.Create();
			world.Add(appliedEntity, new WarResolvedApplied {
				WarId = warId,
				WinnerCountryId = winnerCountryId,
				LoserCountryId = loserCountryId
			});

			return true;
		}

		public static double GetOwnWarProgress(IReadOnlyWorld world, string countryId) {
			if (!TryGetWarId(world, countryId, out string warId, out WarParticipantKind kind)) {
				return 0;
			}
			double rawValue = FindWarProgressValue(world, warId);
			return kind == WarParticipantKind.Attacker ? rawValue : -rawValue;
		}

		static bool TryGetWarId(IReadOnlyWorld world, string countryId, out string warId, out WarParticipantKind kind) {
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].CountryId == countryId) {
						warId = participants[i].WarId;
						kind = participants[i].Kind;
						return true;
					}
				}
			}
			warId = "";
			kind = default;
			return false;
		}

		static string? FindOpponentCountryId(IReadOnlyWorld world, string warId, string countryId) {
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].WarId == warId && participants[i].CountryId != countryId) {
						return participants[i].CountryId;
					}
				}
			}
			return null;
		}

		static double FindWarProgressValue(IReadOnlyWorld world, string warId) {
			int[] warRequired = { TypeId<War>.Value, TypeId<WarProgress>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = arch.GetColumn<War>();
				WarProgress[] progresses = arch.GetColumn<WarProgress>();
				for (int i = 0; i < arch.Count; i++) {
					if (wars[i].WarId == warId) {
						return progresses[i].Value;
					}
				}
			}
			return 0;
		}

		static void DestroyWarAndParticipants(World world, string warId) {
			int[] participantRequired = { TypeId<WarParticipant>.Value };
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
		}
	}
}
