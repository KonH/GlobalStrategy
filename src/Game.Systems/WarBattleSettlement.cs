using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

	namespace GS.Game.Systems {
	public static partial class WarBattleSystem {
		static void FinishBattle(
			World world, WarInfo war, WarBattles.BattleInfo battle,
			WarParticipantKind winner, WarBattleSettings settings,
			ResourceConfig? resourceConfig, DateTime currentTime) {
			ResourceDefinition? progressDefinition =
				resourceConfig?.FindResource(ResourceDefinitions.WarProgress);
			double progressDelta = winner == WarParticipantKind.Attacker
				? settings.BattleProgressGain
				: -settings.BattleProgressGain;
			ResourceMutations.TryApplyClampedDelta(
				world, war.WarId, ResourceDefinitions.WarProgress, progressDelta,
				progressDefinition, $"war_progress_battle_{battle.Value.BattleId}", currentTime,
				-100, 100, out _);

			List<WarBattles.ForceInfo> forces = WarBattles.GetForces(world, battle.Value.BattleId);
			foreach (WarBattles.ForceInfo forceInfo in forces) {
				ref BattleForce force = ref world.Get<BattleForce>(forceInfo.Entity);
				ApplyPopulationCasualties(world, force.CountryId, force.Casualties);
				if (force.Troops > 0) {
					RequireDelta(
						world, force.CountryId, ResourceDefinitions.Recruits,
						force.Troops, 0, double.MaxValue,
						$"war_recruit_return_{battle.Value.BattleId}_{force.CountryId}",
						$"returning survivors from battle '{battle.Value.BattleId}'");
					force.Troops = 0;
				}
			}

			ResolveOccupation(world, battle.Value.TargetProvinceId, winner, forces);
			ref Battle persisted = ref world.Get<Battle>(battle.Entity);
			persisted.Winner = winner;
			persisted.State = BattleState.Finished;

			WarBattles.PruneFinishedBattles(world, war.WarId, settings.MaxFinishedBattlesRetained);
		}

		static void ApplyPopulationCasualties(World world, string countryId, double casualties) {
			double remaining = Math.Ceiling(Math.Max(0, casualties));
			if (remaining <= 0) {
				return;
			}
			var provinces = new List<(string ProvinceId, double Population)>();
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				for (int i = 0; i < arch.Count; i++) {
					if (ownerships[i].OwnerId != countryId) {
						continue;
					}
					double population = RequireResource(
						world, ownerships[i].ProvinceId, ResourceDefinitions.Population,
						$"applying casualties for country '{countryId}'");
					if (population > 0) {
						provinces.Add((ownerships[i].ProvinceId, population));
					}
				}
			}
			provinces.Sort((a, b) => string.CompareOrdinal(a.ProvinceId, b.ProvinceId));
			double totalPopulation = 0;
			foreach (var province in provinces) {
				totalPopulation += province.Population;
			}
			remaining = Math.Min(remaining, totalPopulation);
			double casualtyBudget = remaining;
			foreach (var province in provinces) {
				if (remaining <= 0) {
					break;
				}
				double share = Math.Ceiling(casualtyBudget * province.Population / totalPopulation);
				double deduction = Math.Min(remaining, Math.Min(province.Population, share));
				RequireDelta(
					world, province.ProvinceId, ResourceDefinitions.Population,
					-deduction, 0, double.MaxValue,
					$"war_population_casualty_{province.ProvinceId}",
					$"deducting casualties from province '{province.ProvinceId}'");
				remaining -= deduction;
			}
		}

		static void ResolveOccupation(
			World world, string targetProvinceId, WarParticipantKind winner,
			List<WarBattles.ForceInfo> forces) {
			string winnerCountryId = "";
			var losingCountries = new HashSet<string>(StringComparer.Ordinal);
			foreach (WarBattles.ForceInfo force in forces) {
				if (force.Value.Side == winner && winnerCountryId == "") {
					winnerCountryId = force.Value.CountryId;
				} else if (force.Value.Side != winner) {
					losingCountries.Add(force.Value.CountryId);
				}
			}
			if (winnerCountryId == "") {
				throw new InvalidOperationException(
					$"Cannot resolve occupation for '{targetProvinceId}': winner has no country force.");
			}

			string ownerId = ProvinceOwnershipSystem.GetOwner(world, targetProvinceId);
			string occupierId = ProvinceOccupationSystem.GetOccupier(world, targetProvinceId);
			bool ownerWon = false;
			foreach (WarBattles.ForceInfo force in forces) {
				if (force.Value.Side == winner && force.Value.CountryId == ownerId) {
					ownerWon = true;
					break;
				}
			}
			if (ownerWon) {
				if (losingCountries.Contains(occupierId)) {
					ProvinceOccupationMutations.Clear(world, targetProvinceId);
				}
			} else if (occupierId != winnerCountryId) {
				ProvinceOccupationMutations.Set(world, targetProvinceId, winnerCountryId);
			}
		}
	}
}
