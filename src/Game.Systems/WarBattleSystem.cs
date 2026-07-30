using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

	namespace GS.Game.Systems {
	public static class WarBattleSystem {
		readonly struct WarInfo {
			public readonly int Entity;
			public readonly string WarId;
			public readonly int Capacity;

			public WarInfo(int entity, string warId, int capacity) {
				Entity = entity;
				WarId = warId;
				Capacity = capacity;
			}
		}

		public static void Update(
			World world, DateTime previousTime, DateTime currentTime, Random rng,
			ProvinceTopology topology, WarBattleSettings settings) {
			settings.Validate();
			List<WarInfo> wars = GetWars(world);
			if (wars.Count == 0) {
				return;
			}

			long intervalTicks = checked((long)Math.Round(
				TimeSpan.TicksPerHour * settings.RoundIntervalHours,
				MidpointRounding.AwayFromZero));
			long previousBucket = previousTime.Ticks / intervalTicks;
			long currentBucket = currentTime.Ticks / intervalTicks;
			bool processedBoundary = false;
			for (long bucket = previousBucket + 1; bucket <= currentBucket; bucket++) {
				processedBoundary = true;
				foreach (WarInfo war in wars) {
					FillSlots(world, war, rng, topology, settings);
					List<WarBattles.BattleInfo> active =
						WarBattles.GetBattles(world, war.WarId, BattleState.Active);
					foreach (WarBattles.BattleInfo battle in active) {
						ProcessRound(world, war, battle, rng, settings);
					}
				}
			}

			if (!processedBoundary) {
				foreach (WarInfo war in wars) {
					FillSlots(world, war, rng, topology, settings);
				}
			}
		}

		static List<WarInfo> GetWars(IReadOnlyWorld world) {
			var result = new List<WarInfo>();
			int[] required = { TypeId<War>.Value, TypeId<WarBattleCapacity>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				War[] wars = arch.GetColumn<War>();
				WarBattleCapacity[] capacities = arch.GetColumn<WarBattleCapacity>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add(new WarInfo(
						arch.Entities[i], wars[i].WarId, capacities[i].MaxConcurrentBattleCount));
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.WarId, b.WarId));
			return result;
		}

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

		static void ProcessRound(
			World world, WarInfo war, WarBattles.BattleInfo battle,
			Random rng, WarBattleSettings settings) {
			WarParticipantKind firstSide = rng.Next(2) == 0
				? WarParticipantKind.Attacker
				: WarParticipantKind.Defender;
			WarParticipantKind responseSide = Opposite(firstSide);

			double firstInflicted = Strike(world, battle.Value.BattleId, firstSide, responseSide, rng, settings);
			double responseInflicted = Strike(world, battle.Value.BattleId, responseSide, firstSide, rng, settings);

			bool responseExhausted = GetSideTroops(world, battle.Value.BattleId, responseSide) <= 0;
			bool firstExhausted = GetSideTroops(world, battle.Value.BattleId, firstSide) <= 0;
			if (responseExhausted) {
				FinishBattle(world, war, battle, firstSide, settings);
				return;
			}
			if (firstExhausted) {
				FinishBattle(world, war, battle, responseSide, settings);
				return;
			}

			if (firstInflicted > responseInflicted) {
				AwardInitiative(world, battle.Value.BattleId, firstSide, settings.RoundWinnerInitiativeGain);
			} else if (responseInflicted > firstInflicted) {
				AwardInitiative(world, battle.Value.BattleId, responseSide, settings.RoundWinnerInitiativeGain);
			}
		}

		static double Strike(
			World world, string battleId, WarParticipantKind dealerSide,
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
						world, dealer.CountryId, ResourceDefinitions.Damage,
						$"resolving battle '{battleId}' damage");
					double durability = RequireResource(
						world, target.CountryId, ResourceDefinitions.Durability,
						$"resolving battle '{battleId}' durability");
					if (durability <= 0) {
						throw new InvalidOperationException(
							$"Country '{target.CountryId}' must have positive durability in battle '{battleId}'.");
					}
					double potentialCasualties = dealer.Troops * damage / settings.DamageDivisor;
					double durabilityCoefficient = durability / settings.DurabilityDivisor;
					double randomized = potentialCasualties / durabilityCoefficient
						* NextDouble(rng, settings.CasualtyRandomMin, settings.CasualtyRandomMax);
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
			World world, string battleId, WarParticipantKind side, double gain) {
			foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battleId)) {
				if (force.Value.Side == side) {
					RequireDelta(
						world, force.Value.CountryId, ResourceDefinitions.WarInitiative,
						gain, double.MinValue, double.MaxValue,
						$"awarding round initiative for battle '{battleId}'");
				}
			}
		}

		static void FinishBattle(
			World world, WarInfo war, WarBattles.BattleInfo battle,
			WarParticipantKind winner, WarBattleSettings settings) {
			ref WarProgress progress = ref world.Get<WarProgress>(war.Entity);
			double progressDelta = winner == WarParticipantKind.Attacker
				? settings.BattleProgressGain
				: -settings.BattleProgressGain;
			progress.Value = Math.Clamp(progress.Value + progressDelta, -100, 100);

			List<WarBattles.ForceInfo> forces = WarBattles.GetForces(world, battle.Value.BattleId);
			foreach (WarBattles.ForceInfo forceInfo in forces) {
				ref BattleForce force = ref world.Get<BattleForce>(forceInfo.Entity);
				ApplyPopulationCasualties(world, force.CountryId, force.Casualties);
				if (force.Troops > 0) {
					RequireDelta(
						world, force.CountryId, ResourceDefinitions.Recruits,
						force.Troops, 0, double.MaxValue,
						$"returning survivors from battle '{battle.Value.BattleId}'");
					force.Troops = 0;
				}
			}

			ResolveOccupation(world, battle.Value.TargetProvinceId, winner, forces);
			ref Battle persisted = ref world.Get<Battle>(battle.Entity);
			persisted.Winner = winner;
			persisted.State = BattleState.Finished;
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

		static WarParticipantKind Opposite(WarParticipantKind side) {
			return side == WarParticipantKind.Attacker
				? WarParticipantKind.Defender
				: WarParticipantKind.Attacker;
		}

		static double NextDouble(Random rng, double minimum, double maximum) {
			return minimum + rng.NextDouble() * (maximum - minimum);
		}

		static double RequireResource(
			IReadOnlyWorld world, string ownerId, string resourceId, string operation) {
			if (!ResourceQuery.TryGetValue(world, ownerId, resourceId, out double value)) {
				throw new InvalidOperationException(
					$"Cannot continue {operation}: owner '{ownerId}' is missing resource '{resourceId}'.");
			}
			return value;
		}

		static double RequireDelta(
			World world, string ownerId, string resourceId, double delta,
			double minimum, double maximum, string operation) {
			if (!ResourceMutations.TryApplyClampedDelta(
				world, ownerId, resourceId, delta, minimum, maximum, out double applied)) {
				throw new InvalidOperationException(
					$"Cannot continue {operation}: owner '{ownerId}' is missing resource '{resourceId}'.");
			}
			return applied;
		}
	}
}
