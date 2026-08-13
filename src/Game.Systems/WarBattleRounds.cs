using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

	namespace GS.Game.Systems {
	public static partial class WarBattleSystem {
		static void ProcessRound(
			World world, ResourceQuery resources, WarInfo war, WarBattles.BattleInfo battle,
			Random rng, WarBattleSettings settings,
			ResourceConfig? resourceConfig, DateTime currentTime) {
			WarParticipantKind firstSide = rng.Next(2) == 0
				? WarParticipantKind.Attacker
				: WarParticipantKind.Defender;
			WarParticipantKind responseSide = Opposite(firstSide);

			double firstInflicted = Strike(world, resources, battle.Value.BattleId, firstSide, responseSide, rng, settings);
			double responseInflicted = Strike(world, resources, battle.Value.BattleId, responseSide, firstSide, rng, settings);

			bool responseExhausted = GetSideTroops(world, battle.Value.BattleId, responseSide) <= 0;
			bool firstExhausted = GetSideTroops(world, battle.Value.BattleId, firstSide) <= 0;
			if (responseExhausted) {
				FinishBattle(resources, world, war, battle, firstSide, settings, resourceConfig, currentTime);
				return;
			}
			if (firstExhausted) {
				FinishBattle(resources, world, war, battle, responseSide, settings, resourceConfig, currentTime);
				return;
			}

			if (firstInflicted > responseInflicted) {
				AwardInitiative(world, resources, battle.Value.BattleId, firstSide, settings.RoundWinnerInitiativeGain);
			} else if (responseInflicted > firstInflicted) {
				AwardInitiative(world, resources, battle.Value.BattleId, responseSide, settings.RoundWinnerInitiativeGain);
			}
		}

		static double Strike(
			World world, ResourceQuery resources, string battleId, WarParticipantKind dealerSide,
			WarParticipantKind takerSide, Random rng, WarBattleSettings settings) {
			List<WarBattles.ForceInfo> dealers = WarBattles.GetForces(world, battleId);
			double inflicted = 0;
			foreach (WarBattles.ForceInfo dealerInfo in dealers) {
				if (dealerInfo.Value.Side != dealerSide) {
					continue;
				}
				ref BattleForce dealer = ref world.Get<BattleForce>(dealerInfo.Entity);
				if (dealer.Troops <= 0) {
					continue;
				}
				List<WarBattles.ForceInfo> targets = WarBattles.GetForces(world, battleId);
				foreach (WarBattles.ForceInfo targetInfo in targets) {
					if (targetInfo.Value.Side != takerSide || targetInfo.Value.Troops <= 0) {
						continue;
					}
					ref BattleForce target = ref world.Get<BattleForce>(targetInfo.Entity);
					double damage = RequireResource(
						resources, world, dealer.CountryId, ResourceDefinitions.Damage,
						$"resolving battle '{battleId}' damage");
					double durability = RequireResource(
						resources, world, target.CountryId, ResourceDefinitions.Durability,
						$"resolving battle '{battleId}' durability");
					if (durability <= 0) {
						throw new InvalidOperationException(
							$"Country '{target.CountryId}' must have positive durability in battle '{battleId}'.");
					}
					double potentialCasualties = dealer.Troops * damage / settings.DamageDivisor;
					double durabilityCoefficient = durability / settings.DurabilityDivisor;
					double randomized = potentialCasualties / durabilityCoefficient
						* NextDouble(rng, settings.CasualtyRandomMin, settings.CasualtyRandomMax)
						* settings.CasualtyCoefficient;
					double casualties = Math.Ceiling(Math.Max(
						randomized,
						Math.Max(
							target.Troops * settings.MinimumCasualtyFraction,
							settings.MinimumAbsoluteCasualties)));
					casualties = Math.Clamp(casualties, 0, target.Troops);
					target.Troops -= casualties;
					target.Casualties += casualties;
					inflicted += casualties;
					break;
				}
			}
			return inflicted;
		}

		static double GetSideTroops(
			IReadOnlyWorld world, string battleId, WarParticipantKind side) {
			double result = 0;
			foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battleId)) {
				if (force.Value.Side == side) {
					result += force.Value.Troops;
				}
			}
			return result;
		}

		static void AwardInitiative(
			World world, ResourceQuery resources, string battleId, WarParticipantKind side, double gain) {
			foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battleId)) {
				if (force.Value.Side == side) {
					RequireDelta(
						resources, world, force.Value.CountryId, ResourceDefinitions.WarInitiative,
						gain, double.MinValue, double.MaxValue,
						$"war_round_initiative_{battleId}_{force.Value.CountryId}",
						$"awarding round initiative for battle '{battleId}'");
				}
			}
		}
	}
}
