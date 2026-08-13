using System;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class WarWinChanceEstimator {
		const double DurabilityFloor = 1.0;

		/// <summary>
		/// Pure numeric win-% from combat stats. Pending revenge bonuses multiply attacker
		/// damage/durability (inputs are treated as base without live revenge residue).
		/// </summary>
		public static int EstimateAttackerWinPercent(
			double attackerRecruits,
			double attackerDamage,
			double attackerDurability,
			double defenderRecruits,
			double defenderDamage,
			double defenderDurability,
			double pendingAttackerDamageBonusPercent = 0,
			double pendingAttackerDurabilityBonusPercent = 0) {
			if (attackerRecruits == 0) {
				return 1;
			}

			double effectiveAttackerDamage = ApplyPendingBonus(attackerDamage, pendingAttackerDamageBonusPercent);
			double effectiveAttackerDurability = ApplyPendingBonus(attackerDurability, pendingAttackerDurabilityBonusPercent);
			double attackerStrength = SideStrength(attackerRecruits, effectiveAttackerDamage, defenderDurability);
			double defenderStrength = SideStrength(defenderRecruits, defenderDamage, effectiveAttackerDurability);
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

		public static int EstimateAttackerWinPercent(
			IReadOnlyWorld world,
			ResourceQuery resources,
			string attackerCountryId,
			string defenderCountryId,
			double pendingAttackerDamageBonusPercent = 0,
			double pendingAttackerDurabilityBonusPercent = 0) {
			double attackerRecruits = resources.GetValue(world, attackerCountryId, ResourceDefinitions.Recruits);
			if (attackerRecruits == 0) {
				return 1;
			}

			double defenderRecruits = resources.GetValue(world, defenderCountryId, ResourceDefinitions.Recruits);
			double attackerDamage = EffectiveCombatStat(
				world,
				resources,
				attackerCountryId,
				ResourceDefinitions.Damage,
				"damage",
				pendingAttackerDamageBonusPercent);
			double attackerDurability = EffectiveCombatStat(
				world,
				resources,
				attackerCountryId,
				ResourceDefinitions.Durability,
				"durability",
				pendingAttackerDurabilityBonusPercent);
			double defenderDamage = resources.GetValue(world, defenderCountryId, ResourceDefinitions.Damage);
			double defenderDurability = resources.GetValue(world, defenderCountryId, ResourceDefinitions.Durability);

			// EffectiveCombatStat already applied pending (and stripped live revenge); pass 0 pending.
			return EstimateAttackerWinPercent(
				attackerRecruits,
				attackerDamage,
				attackerDurability,
				defenderRecruits,
				defenderDamage,
				defenderDurability);
		}

		static double ApplyPendingBonus(double live, double pendingBonusPercent) {
			if (pendingBonusPercent <= 0) {
				return live;
			}
			return live * (1.0 + pendingBonusPercent / 100.0);
		}

		static double EffectiveCombatStat(
			IReadOnlyWorld world,
			ResourceQuery resources,
			string countryId,
			string resourceId,
			string revengeKind,
			double pendingBonusPercent) {
			double live = resources.GetValue(world, countryId, resourceId);
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
