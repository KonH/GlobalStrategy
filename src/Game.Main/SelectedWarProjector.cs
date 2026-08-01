using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public static class SelectedWarProjector {
		public static double ComputeActiveBattleProgress(double attackerTroops, double defenderTroops) {
			return WarProgressSnapshot.ComputeActiveBattleProgress(attackerTroops, defenderTroops);
		}

		public static void Project(IReadOnlyWorld world, SelectedWarState state, CountryConfig? countryConfig = null) {
			string warId = state.PendingWarId;
			if (string.IsNullOrEmpty(warId) || !WarExists(world, warId)) {
				state.SetInvalid();
				return;
			}

			var participants = WarBattles.GetParticipants(world, warId);
			string attackerCountryId = GetFirstCountry(participants, WarParticipantKind.Attacker);
			string defenderCountryId = GetFirstCountry(participants, WarParticipantKind.Defender);
			if (attackerCountryId == "" || defenderCountryId == "") {
				state.SetInvalid();
				return;
			}

			double progress = ResourceQuery.GetValue(world, warId, ResourceDefinitions.WarProgress);
			List<WarProgressHistoryEntryState> history = MapHistory(WarProgressSnapshot.BuildHistory(world, warId));
			WarSideStatsState attacker = MapSideStats(
				WarProgressSnapshot.BuildSideStats(
					world, warId, attackerCountryId, WarParticipantKind.Attacker, countryConfig));
			WarSideStatsState defender = MapSideStats(
				WarProgressSnapshot.BuildSideStats(
					world, warId, defenderCountryId, WarParticipantKind.Defender, countryConfig));
			List<WarBattleRowState> battles = MapBattles(WarProgressSnapshot.BuildBattles(world, warId));

			state.Set(true, warId, progress, history, attacker, defender, battles);
		}

		public static List<WarProgressHistoryEntryState> MapHistory(List<WarProgressHistorySnapshot> snapshots) {
			var result = new List<WarProgressHistoryEntryState>(snapshots.Count);
			foreach (WarProgressHistorySnapshot snapshot in snapshots) {
				result.Add(new WarProgressHistoryEntryState(
					snapshot.EffectId, snapshot.AppliedDelta, snapshot.Timestamp));
			}
			return result;
		}

		public static WarSideStatsState MapSideStats(WarSideStatsSnapshot snapshot) {
			var effects = new List<EffectStateEntry>(snapshot.DamageBonusEffects?.Count ?? 0);
			if (snapshot.DamageBonusEffects != null) {
				foreach (WarEffectSnapshot effect in snapshot.DamageBonusEffects) {
					effects.Add(new EffectStateEntry(
						effect.EffectId, effect.Value, effect.PayType,
						effect.MaxTotal, effect.OrgDisplayName));
				}
			}
			return new WarSideStatsState(
				snapshot.CountryId,
				snapshot.Recruits,
				snapshot.TroopsInBattles,
				snapshot.Casualties,
				snapshot.Damage,
				snapshot.Durability,
				snapshot.DamageBase,
				snapshot.DamageRulerBonus,
				snapshot.DamageAdvisorBonus,
				snapshot.DamageBonusPercent,
				effects,
				snapshot.DurabilityBase,
				snapshot.DurabilityRulerBonus,
				snapshot.DurabilityAdvisorBonus);
		}

		public static List<WarBattleRowState> MapBattles(List<WarBattleRowSnapshot> snapshots) {
			var result = new List<WarBattleRowState>(snapshots.Count);
			foreach (WarBattleRowSnapshot snapshot in snapshots) {
				result.Add(new WarBattleRowState(
					snapshot.BattleId,
					snapshot.ProvinceId,
					snapshot.IsFinished,
					snapshot.WinnerCountryId,
					snapshot.WinnerSide,
					snapshot.AttackerCasualties,
					snapshot.DefenderCasualties,
					snapshot.Progress,
					snapshot.AttackerTroops,
					snapshot.DefenderTroops));
			}
			return result;
		}

		static bool WarExists(IReadOnlyWorld world, string warId) {
			int[] required = { TypeId<War>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				War[] wars = archetype.GetColumn<War>();
				for (int i = 0; i < archetype.Count; i++) {
					if (wars[i].WarId == warId) {
						return true;
					}
				}
			}
			return false;
		}

		static string GetFirstCountry(List<WarBattles.ParticipantInfo> participants, WarParticipantKind side) {
			foreach (WarBattles.ParticipantInfo participant in participants) {
				if (participant.Side == side) {
					return participant.CountryId;
				}
			}
			return "";
		}
	}
}
