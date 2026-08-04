using System;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class WarWinChanceEstimator {
		const double DurabilityFloor = 1.0;

		public static int EstimateAttackerWinPercent(
			IReadOnlyWorld world,
			string attackerCountryId,
			string defenderCountryId,
			double pendingAttackerDamageBonusPercent = 0,
			double pendingAttackerDurabilityBonusPercent = 0) {
			double attackerRecruits = ResourceQuery.GetValue(world, attackerCountryId, ResourceDefinitions.Recruits);
			if (attackerRecruits == 0) {
				return 1;
			}

			double defenderRecruits = ResourceQuery.GetValue(world, defenderCountryId, ResourceDefinitions.Recruits);
			double attackerDamage = EffectiveCombatStat(
				world,
				attackerCountryId,
				ResourceDefinitions.Damage,
				"damage",
				pendingAttackerDamageBonusPercent);
			double attackerDurability = EffectiveCombatStat(
				world,
				attackerCountryId,
				ResourceDefinitions.Durability,
				"durability",
				pendingAttackerDurabilityBonusPercent);
			double defenderDamage = ResourceQuery.GetValue(world, defenderCountryId, ResourceDefinitions.Damage);
			double defenderDurability = ResourceQuery.GetValue(world, defenderCountryId, ResourceDefinitions.Durability);

			double attackerStrength = SideStrength(attackerRecruits, attackerDamage, defenderDurability);
			double defenderStrength = SideStrength(defenderRecruits, defenderDamage, attackerDurability);
			if (attackerStrength == 0 && defenderStrength == 0) {
				return 50;
			}

			double winFraction = attackerStrength / (attackerStrength + defenderStrength);
			int percent = (int)Math.Round(winFraction * 100.0);
			if (percent < 1) {
				return 1;
			}
			if (percent > 99) {
				return 99;
			}
			return percent;
		}

		static double EffectiveCombatStat(
			IReadOnlyWorld world,
			string countryId,
			string resourceId,
			string revengeKind,
			double pendingBonusPercent) {
			double live = ResourceQuery.GetValue(world, countryId, resourceId);
			if (pendingBonusPercent <= 0) {
				return live;
			}

			double liveRevengePercent = RevengeWarBonusQuery.GetBonusPercent(world, countryId, revengeKind);
			double withoutLiveRevenge = liveRevengePercent == 0
				? live
				: live / (1.0 + liveRevengePercent / 100.0);
			return withoutLiveRevenge * (1.0 + pendingBonusPercent / 100.0);
		}

		static double SideStrength(double recruits, double damage, double enemyDurability) {
			return recruits * damage / Math.Max(enemyDurability, DurabilityFloor);
		}
	}
}
