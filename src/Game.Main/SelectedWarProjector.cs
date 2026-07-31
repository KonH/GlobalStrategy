using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public static class SelectedWarProjector {
		public static double ComputeActiveBattleProgress(double attackerTroops, double defenderTroops) {
			if (attackerTroops == 0 && defenderTroops == 0) {
				return 0;
			}
			double total = attackerTroops + defenderTroops;
			return Math.Clamp(100 * (attackerTroops - defenderTroops) / total, -100, 100);
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
			List<WarProgressHistoryEntryState> history = BuildHistory(world, warId);
			WarSideStatsState attacker = BuildSideStats(
				world, warId, attackerCountryId, WarParticipantKind.Attacker, countryConfig);
			WarSideStatsState defender = BuildSideStats(
				world, warId, defenderCountryId, WarParticipantKind.Defender, countryConfig);
			List<WarBattleRowState> battles = BuildBattles(world, warId);

			state.Set(true, warId, progress, history, attacker, defender, battles);
		}

		static WarSideStatsState BuildSideStats(
			IReadOnlyWorld world, string warId, string countryId, WarParticipantKind side, CountryConfig? countryConfig) {
			double troopsInBattles = 0;
			double casualties = 0;
			foreach (WarBattles.BattleInfo battle in WarBattles.GetBattles(world, warId)) {
				foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battle.Value.BattleId)) {
					if (force.Value.Side != side) {
						continue;
					}
					if (battle.Value.State == BattleState.Active) {
						troopsInBattles += force.Value.Troops;
					} else if (battle.Value.State == BattleState.Finished) {
						casualties += force.Value.Casualties;
					}
				}
			}

			CountryEntry? countryEntry = countryConfig?.FindByCountryId(countryId);
			double damageBase = countryEntry?.BaseDamage ?? 40;
			double damageRulerBonus = WartimeSkillQuery.GetSkill(world, countryId, "ruler", "power");
			double damageAdvisorBonus = WartimeSkillQuery.GetSkill(world, countryId, "military_advisor", "power");
			double damageBonusPercent = ResourceQuery.GetValue(world, countryId, ResourceDefinitions.TroopsDamageBonusPercent);
			double durabilityBase = countryEntry?.BaseDurability ?? 40;
			double durabilityRulerBonus = WartimeSkillQuery.GetSkill(world, countryId, "ruler", "stinginess");
			double durabilityAdvisorBonus = WartimeSkillQuery.GetSkill(world, countryId, "economic_advisor", "stinginess");

			return new WarSideStatsState(
				countryId,
				ResourceQuery.GetValue(world, countryId, ResourceDefinitions.Recruits),
				troopsInBattles,
				casualties,
				ResourceQuery.GetValue(world, countryId, ResourceDefinitions.Damage),
				ResourceQuery.GetValue(world, countryId, ResourceDefinitions.Durability),
				damageBase,
				damageRulerBonus,
				damageAdvisorBonus,
				damageBonusPercent,
				BuildEffects(world, countryId, ResourceDefinitions.TroopsDamageBonusPercent),
				durabilityBase,
				durabilityRulerBonus,
				durabilityAdvisorBonus);
		}

		static List<EffectStateEntry> BuildEffects(IReadOnlyWorld world, string ownerId, string resourceId) {
			var result = new List<EffectStateEntry>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != ownerId || links[i].ResourceId != resourceId) {
						continue;
					}
					result.Add(new EffectStateEntry(
						effects[i].EffectId, effects[i].Value, effects[i].PayType,
						effects[i].MaxTotal, GetOrgDisplayName(world, effects[i].OrgId)));
				}
			}
			return result;
		}

		static string GetOrgDisplayName(IReadOnlyWorld world, string orgId) {
			if (string.IsNullOrEmpty(orgId)) {
				return "";
			}
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrganizationId == orgId) {
						return string.IsNullOrEmpty(orgs[i].DisplayName) ? orgId : orgs[i].DisplayName;
					}
				}
			}
			return orgId;
		}

		static List<WarBattleRowState> BuildBattles(IReadOnlyWorld world, string warId) {
			var rows = new List<WarBattleRowState>();
			foreach (WarBattles.BattleInfo battle in WarBattles.GetBattles(world, warId)) {
				double attackerTroops = 0;
				double defenderTroops = 0;
				double attackerCasualties = 0;
				double defenderCasualties = 0;
				string winnerCountryId = "";
				foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battle.Value.BattleId)) {
					if (force.Value.Side == WarParticipantKind.Attacker) {
						attackerTroops += force.Value.Troops;
						attackerCasualties += force.Value.Casualties;
					} else {
						defenderTroops += force.Value.Troops;
						defenderCasualties += force.Value.Casualties;
					}
					if (battle.Value.State == BattleState.Finished
						&& force.Value.Side == battle.Value.Winner
						&& winnerCountryId == "") {
						winnerCountryId = force.Value.CountryId;
					}
				}

				if (battle.Value.State == BattleState.Finished) {
					rows.Add(new WarBattleRowState(
						battle.Value.BattleId,
						battle.Value.TargetProvinceId,
						true,
						winnerCountryId,
						battle.Value.Winner,
						attackerCasualties,
						defenderCasualties,
						0,
						0,
						0));
				} else {
					rows.Add(new WarBattleRowState(
						battle.Value.BattleId,
						battle.Value.TargetProvinceId,
						false,
						"",
						default,
						0,
						0,
						ComputeActiveBattleProgress(attackerTroops, defenderTroops),
						attackerTroops,
						defenderTroops));
				}
			}
			return rows;
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

		static List<WarProgressHistoryEntryState> BuildHistory(IReadOnlyWorld world, string warId) {
			var result = new List<WarProgressHistoryEntryState>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value,
				TypeId<ResourceHistory>.Value
			};
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				ResourceHistory[] histories = archetype.GetColumn<ResourceHistory>();
				for (int i = 0; i < archetype.Count; i++) {
					if (owners[i].OwnerId != warId || resources[i].ResourceId != ResourceDefinitions.WarProgress) {
						continue;
					}
					List<ResourceChangeEntry>? entries = histories[i].History;
					if (entries == null) {
						return result;
					}
					foreach (ResourceChangeEntry entry in entries) {
						result.Add(new WarProgressHistoryEntryState(entry.EffectId, entry.AppliedDelta, entry.Timestamp));
					}
					return result;
				}
			}
			return result;
		}
	}
}
