using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

	namespace GS.Game.Systems {
	public static partial class WarBattleSystem {
		static void FillSlots(
			World world, WarInfo war, Random rng, ProvinceTopology topology,
			WarBattleSettings settings) {
			while (WarBattles.GetBattles(world, war.WarId, BattleState.Active).Count < war.Capacity) {
				List<WarBattles.ParticipantInfo> participants = WarBattles.GetParticipants(world, war.WarId);
				if (participants.Count == 0) {
					return;
				}
				WarBattles.ParticipantInfo initiator = ChooseInitiator(world, participants, rng);
				RequireDelta(
					world, initiator.CountryId, ResourceDefinitions.WarInitiative,
					-settings.InitiationCost, double.MinValue, double.MaxValue,
					$"war_initiation_{war.WarId}_{initiator.CountryId}",
					$"charging battle initiation for war '{war.WarId}'");

				string? targetProvinceId = SelectTarget(
					world, war.WarId, initiator, participants, rng, topology, settings);
				if (targetProvinceId == null) {
					return;
				}

				string battleId = $"{war.WarId}_battle_{WarBattles.GetBattles(world, war.WarId).Count}";
				foreach (WarBattles.ParticipantInfo participant in participants) {
					double available = RequireResource(
						world, participant.CountryId, ResourceDefinitions.Recruits,
						$"allocating forces for battle '{battleId}'");
					available = Math.Max(0, available);
					double baseRatio = 1.0 / (war.Capacity + settings.TroopDenominatorOffset);
					double randomizedRatio = baseRatio
						* NextDouble(rng, settings.TroopRandomMin, settings.TroopRandomMax);
					double troops = Math.Clamp(Math.Ceiling(available * randomizedRatio), 0, available);
					RequireDelta(
						world, participant.CountryId, ResourceDefinitions.Recruits,
						-troops, 0, double.MaxValue,
						$"war_recruit_commit_{battleId}_{participant.CountryId}",
						$"committing recruits to battle '{battleId}'");

					int forceEntity = world.Create();
					world.Add(forceEntity, new BattleForce {
						BattleId = battleId,
						CountryId = participant.CountryId,
						Side = participant.Side,
						Troops = troops,
						Casualties = 0
					});
				}

				int battleEntity = world.Create();
				world.Add(battleEntity, new Battle {
					BattleId = battleId,
					WarId = war.WarId,
					TargetProvinceId = targetProvinceId,
					State = BattleState.Active,
					Winner = default
				});
			}
		}

		static WarBattles.ParticipantInfo ChooseInitiator(
			IReadOnlyWorld world, List<WarBattles.ParticipantInfo> participants, Random rng) {
			double maximum = double.MinValue;
			var tied = new List<WarBattles.ParticipantInfo>();
			foreach (WarBattles.ParticipantInfo participant in participants) {
				double initiative = RequireResource(
					world, participant.CountryId, ResourceDefinitions.WarInitiative,
					$"selecting the initiator for war '{participant.CountryId}'");
				if (initiative > maximum) {
					maximum = initiative;
					tied.Clear();
					tied.Add(participant);
				} else if (initiative == maximum) {
					tied.Add(participant);
				}
			}
			return tied[rng.Next(tied.Count)];
		}

		static string? SelectTarget(
			IReadOnlyWorld world, string warId, WarBattles.ParticipantInfo initiator,
			List<WarBattles.ParticipantInfo> participants, Random rng,
			ProvinceTopology topology, WarBattleSettings settings) {
			var enemyCountries = new HashSet<string>(StringComparer.Ordinal);
			foreach (WarBattles.ParticipantInfo participant in participants) {
				if (participant.Side != initiator.Side) {
					enemyCountries.Add(participant.CountryId);
				}
			}
			var activeTargets = WarBattles.GetActiveTargetIds(world, warId);
			var origins = new List<string>();
			var eligible = new HashSet<string>(StringComparer.Ordinal);
			foreach (string provinceId in topology.ProvinceIds) {
				string ownerId = ProvinceOwnershipSystem.GetOwner(world, provinceId);
				string occupierId = ProvinceOccupationSystem.GetOccupier(world, provinceId);
				if (ownerId == initiator.CountryId || occupierId == initiator.CountryId) {
					origins.Add(provinceId);
				}
				if (enemyCountries.Contains(ownerId)
					&& occupierId != initiator.CountryId
					&& !activeTargets.Contains(provinceId)) {
					eligible.Add(provinceId);
				}
			}

			var primary = new HashSet<string>(StringComparer.Ordinal);
			foreach (string origin in origins) {
				foreach (string neighbor in topology.GetNeighbors(origin)) {
					if (eligible.Contains(neighbor)) {
						primary.Add(neighbor);
					}
				}
			}
			if (primary.Count > 0) {
				var ordered = new List<string>(primary);
				ordered.Sort(StringComparer.Ordinal);
				return ordered[rng.Next(ordered.Count)];
			}
			if (origins.Count == 0 || eligible.Count == 0) {
				return null;
			}

			var ranked = new List<(string ProvinceId, double Distance)>();
			foreach (string candidate in eligible) {
				double distance = double.MaxValue;
				foreach (string origin in origins) {
					distance = Math.Min(distance, topology.GetSquaredDistance(origin, candidate));
				}
				ranked.Add((candidate, distance));
			}
			ranked.Sort((a, b) => {
				int distance = a.Distance.CompareTo(b.Distance);
				return distance != 0
					? distance
					: string.CompareOrdinal(a.ProvinceId, b.ProvinceId);
			});
			int candidateCount = Math.Min(settings.FallbackCandidateCount, ranked.Count);
			return ranked[rng.Next(candidateCount)].ProvinceId;
		}
	}
}
