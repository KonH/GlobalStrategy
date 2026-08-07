using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class WarProgressSnapshot {
		public static double ComputeActiveBattleProgress(double attackerTroops, double defenderTroops) {
			if (attackerTroops == 0 && defenderTroops == 0) {
				return 0;
			}
			double total = attackerTroops + defenderTroops;
			return Math.Clamp(100 * (attackerTroops - defenderTroops) / total, -100, 100);
		}

		public static List<WarProgressHistorySnapshot> BuildHistory(IReadOnlyWorld world, string warId) {
			var result = new List<WarProgressHistorySnapshot>();
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
						result.Add(new WarProgressHistorySnapshot {
							EffectId = entry.EffectId,
							AppliedDelta = entry.AppliedDelta,
							Timestamp = entry.Timestamp
						});
					}
					return result;
				}
			}
			return result;
		}

		public static WarSideStatsSnapshot BuildSideStats(
			IReadOnlyWorld world,
			ResourceQuery resources,
			string warId,
			string countryId,
			WarParticipantKind side,
			CountryConfig? countryConfig) {
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
			double damageRulerBonus = WartimeSkillQuery.GetSkill(world, countryId, "ruler", "power", resources);
			double damageAdvisorBonus = WartimeSkillQuery.GetSkill(world, countryId, "military_advisor", "power", resources);
			double damageBonusPercent = resources.GetValue(world, countryId, ResourceDefinitions.TroopsDamageBonusPercent);
			double durabilityBase = countryEntry?.BaseDurability ?? 40;
			double durabilityRulerBonus = WartimeSkillQuery.GetSkill(world, countryId, "ruler", "stinginess", resources);
			double durabilityAdvisorBonus = WartimeSkillQuery.GetSkill(world, countryId, "economic_advisor", "stinginess", resources);

			return new WarSideStatsSnapshot {
				CountryId = countryId,
				Recruits = resources.GetValue(world, countryId, ResourceDefinitions.Recruits),
				TroopsInBattles = troopsInBattles,
				Casualties = casualties,
				Damage = resources.GetValue(world, countryId, ResourceDefinitions.Damage),
				Durability = resources.GetValue(world, countryId, ResourceDefinitions.Durability),
				DamageBase = damageBase,
				DamageRulerBonus = damageRulerBonus,
				DamageAdvisorBonus = damageAdvisorBonus,
				DamageBonusPercent = damageBonusPercent,
				DamageBonusEffects = BuildEffects(world, countryId, ResourceDefinitions.TroopsDamageBonusPercent),
				DurabilityBase = durabilityBase,
				DurabilityRulerBonus = durabilityRulerBonus,
				DurabilityAdvisorBonus = durabilityAdvisorBonus
			};
		}

		public static List<WarEffectSnapshot> BuildEffects(IReadOnlyWorld world, string ownerId, string resourceId) {
			var result = new List<WarEffectSnapshot>();
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
					result.Add(new WarEffectSnapshot {
						EffectId = effects[i].EffectId,
						Value = effects[i].Value,
						PayType = effects[i].PayType,
						MaxTotal = effects[i].MaxTotal,
						OrgDisplayName = GetOrgDisplayName(world, effects[i].OrgId)
					});
				}
			}
			return result;
		}

		public static List<WarBattleRowSnapshot> BuildBattles(IReadOnlyWorld world, string warId) {
			var rows = new List<WarBattleRowSnapshot>();
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
					rows.Add(new WarBattleRowSnapshot {
						BattleId = battle.Value.BattleId,
						ProvinceId = battle.Value.TargetProvinceId,
						IsFinished = true,
						WinnerCountryId = winnerCountryId,
						WinnerSide = battle.Value.Winner,
						AttackerCasualties = attackerCasualties,
						DefenderCasualties = defenderCasualties,
						Progress = 0,
						AttackerTroops = 0,
						DefenderTroops = 0
					});
				} else {
					rows.Add(new WarBattleRowSnapshot {
						BattleId = battle.Value.BattleId,
						ProvinceId = battle.Value.TargetProvinceId,
						IsFinished = false,
						WinnerCountryId = "",
						WinnerSide = default,
						AttackerCasualties = 0,
						DefenderCasualties = 0,
						Progress = ComputeActiveBattleProgress(attackerTroops, defenderTroops),
						AttackerTroops = attackerTroops,
						DefenderTroops = defenderTroops
					});
				}
			}
			return rows;
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
	}
}
