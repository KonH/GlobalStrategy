using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

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

		public static bool IsWarFree(IReadOnlyWorld world, string countryId, string hqCountryId) {
			if (!string.IsNullOrEmpty(countryId) && IsInWar(world, countryId)) {
				return false;
			}
			if (!string.IsNullOrEmpty(hqCountryId) && IsInWar(world, hqCountryId)) {
				return false;
			}
			return true;
		}

		public static bool IsWarFree(
			IReadOnlyWorld world,
			string countryId,
			string orgId,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId) {
			string hqCountryId = "";
			if (hqCountryByOrgId != null && !string.IsNullOrEmpty(orgId)) {
				hqCountryByOrgId.TryGetValue(orgId, out hqCountryId);
				hqCountryId ??= "";
			}
			return IsWarFree(world, countryId, hqCountryId);
		}

		public static List<string> GetOpponentCountryIds(IReadOnlyWorld world, string countryId) {
			var result = new List<string>();
			string? warId = FindWarIdForCountry(world, countryId);
			if (warId == null) {
				return result;
			}
			foreach (WarBattles.ParticipantInfo participant in WarBattles.GetParticipants(world, warId)) {
				if (participant.CountryId != countryId) {
					result.Add(participant.CountryId);
				}
			}
			return result;
		}

		public static bool DeclareWar(World world, ResourceQuery resources, string attackerCountryId, string defenderCountryId, DateTime currentTime) {
			return DeclareWar(
				world, resources, attackerCountryId, defenderCountryId, currentTime,
				new ProvinceTopology(new ProvinceConfig()), new WarBattleSettings(), out _);
		}

		public static bool DeclareWar(
			World world, ResourceQuery resources, string attackerCountryId, string defenderCountryId, DateTime currentTime,
			out string? warId) {
			return DeclareWar(
				world, resources, attackerCountryId, defenderCountryId, currentTime,
				new ProvinceTopology(new ProvinceConfig()), new WarBattleSettings(), out warId);
		}

		public static bool DeclareWar(
			World world, ResourceQuery resources, string attackerCountryId, string defenderCountryId, DateTime currentTime,
			ProvinceTopology topology, WarBattleSettings settings) {
			return DeclareWar(world, resources, attackerCountryId, defenderCountryId, currentTime, topology, settings, out _);
		}

		public static bool DeclareWar(
			World world, ResourceQuery resources, string attackerCountryId, string defenderCountryId, DateTime currentTime,
			ProvinceTopology topology, WarBattleSettings settings, out string? warId) {
			warId = null;
			if (attackerCountryId == defenderCountryId) {
				return false;
			}
			if (IsInWar(world, attackerCountryId) || IsInWar(world, defenderCountryId)) {
				return false;
			}

			warId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}";

			int warEntity = world.Create();
			world.Add(warEntity, new War { WarId = warId, DeclaredAt = currentTime });
			world.Add(warEntity, new WarBattleCapacity {
				MaxConcurrentBattleCount = CalculateCapacity(
					world, attackerCountryId, defenderCountryId, topology, settings)
			});

			resources.Set(world, warId, ResourceDefinitions.WarProgress, 0, OwnerType.War);
			int progressEntity = resources.FindEntity(world, warId, ResourceDefinitions.WarProgress);
			if (progressEntity >= 0) {
				world.Add(progressEntity, new ResourceHistory {
					History = new List<ResourceChangeEntry>()
				});
			}

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

			ResourceMutations.TrySetValue(
				resources, world, attackerCountryId, ResourceDefinitions.WarInitiative,
				settings.AttackerInitialInitiative, out _);
			ResourceMutations.TrySetValue(
				resources, world, defenderCountryId, ResourceDefinitions.WarInitiative,
				settings.DefenderInitialInitiative, out _);

			return true;
		}

		public static bool ResolveWar(
			World world,
			ResourceQuery resources,
			string countryId,
			WarOutcome outcomeForCountry,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig = null) {
			string? warId = FindWarIdForCountry(world, countryId);
			if (warId == null) {
				return false;
			}
			if (!TryGetWarState(resources, world, warId, out string attackerId, out string defenderId, out double progress, out DateTime declaredAt)) {
				return false;
			}
			string opponentCountryId = attackerId == countryId ? defenderId : attackerId;
			string winnerCountryId = outcomeForCountry == WarOutcome.Win ? countryId : opponentCountryId;
			string loserCountryId = outcomeForCountry == WarOutcome.Win ? opponentCountryId : countryId;

			ApplyWarResolution(
				world, resources, warId, attackerId, defenderId, winnerCountryId, loserCountryId, progress,
				declaredAt, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
			return true;
		}

		public static double GetOwnWarProgress(IReadOnlyWorld world, ResourceQuery resources, string countryId) {
			string? warId = FindWarIdForCountry(world, countryId);
			if (warId == null) {
				return 0;
			}
			if (!TryGetWarProgress(resources, world, warId, out double progress)) {
				return 0;
			}
			bool isAttacker = false;
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].WarId == warId && participants[i].CountryId == countryId) {
						isAttacker = participants[i].Kind == WarParticipantKind.Attacker;
					}
				}
			}
			return isAttacker ? progress : -progress;
		}

		public static bool StopWar(
			World world,
			ResourceQuery resources,
			string countryId,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig = null) {
			string? warId = FindWarIdForCountry(world, countryId);
			if (warId == null) {
				return false;
			}
			ResolvePeace(
				world, resources, warId, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
			return true;
		}

		public static void TryResolvePeaceByChance(
			World world,
			ResourceQuery resources,
			DateTime previousTime,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig = null) {
			bool isDayBoundary = previousTime.Date != currentTime.Date;
			if (!isDayBoundary) {
				return;
			}

			var warIds = new List<string>();
			int[] warRequired = { TypeId<War>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = arch.GetColumn<War>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					warIds.Add(wars[i].WarId);
				}
			}

			var toResolve = new List<string>();
			foreach (string warId in warIds) {
				if (!TryGetWarProgress(resources, world, warId, out double progress)) {
					continue;
				}
				double chance = ComputePeaceChancePercent(progress, settings);
				if (chance <= 0) {
					continue;
				}
				if (rng.NextDouble() * 100.0 < chance) {
					toResolve.Add(warId);
				}
			}

			foreach (string warId in toResolve) {
				if (TryGetWarProgress(resources, world, warId, out _)) {
					ResolvePeace(
						world, resources, warId, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
				}
			}
		}

		public static void ResolvePeace(
			World world,
			ResourceQuery resources,
			string warId,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig = null) {
			if (!TryGetWarState(resources, world, warId, out string attackerId, out string defenderId, out double progress, out DateTime declaredAt)) {
				return;
			}

			if (progress == 0.0) {
				ClearOccupationForParticipants(world, attackerId, defenderId);
				DestroyWar(world, resources, warId);
				return;
			}

			string winnerId = progress > 0.0 ? attackerId : defenderId;
			string loserId = progress > 0.0 ? defenderId : attackerId;
			ApplyWarResolution(
				world, resources, warId, attackerId, defenderId, winnerId, loserId, progress,
				declaredAt, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
		}

		static void ApplyWarResolution(
			World world,
			ResourceQuery resources,
			string warId,
			string attackerId,
			string defenderId,
			string winnerId,
			string loserId,
			double progress,
			DateTime declaredAt,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig) {
			List<WarProgressHistorySnapshot> history = WarProgressSnapshot.BuildHistory(world, warId);
			WarSideStatsSnapshot attackerStats = WarProgressSnapshot.BuildSideStats(
				world, resources, warId, attackerId, WarParticipantKind.Attacker, countryConfig);
			WarSideStatsSnapshot defenderStats = WarProgressSnapshot.BuildSideStats(
				world, resources, warId, defenderId, WarParticipantKind.Defender, countryConfig);
			List<WarBattleRowSnapshot> battles = WarProgressSnapshot.BuildBattles(world, warId);

			List<WarProvinceTransferSnapshot> transferredProvinces = TransferOccupiedProvinces(
				world, winnerId, loserId, rng, settings, topology, provinceCenters);
			ClearOccupationForParticipants(world, attackerId, defenderId);
			(double goldTaken, List<WarGoldRecipientSnapshot> goldRecipients) = TransferGoldSpoils(
				world, resources, winnerId, loserId, declaredAt, currentTime, settings, maxControlPool);
			List<WarControlDeltaSnapshot> controlDeltas = ApplyControlShifts(
				world, winnerId, loserId, settings, maxControlPool);
			DestroyWar(world, resources, warId);

			int appliedEntity = world.Create();
			world.Add(appliedEntity, new WarResolvedApplied {
				WarId = warId,
				AttackerCountryId = attackerId,
				DefenderCountryId = defenderId,
				WinnerCountryId = winnerId,
				LoserCountryId = loserId,
				Progress = progress,
				GoldTaken = goldTaken,
				GoldRecipients = goldRecipients,
				ControlDeltas = controlDeltas,
				TransferredProvinces = transferredProvinces,
				History = history,
				Attacker = attackerStats,
				Defender = defenderStats,
				Battles = battles
			});
			RevengeEligibilityQuery.OnWarResolved(world, winnerId, loserId);
		}

		public static double ComputePeaceChancePercent(double progress, GameSettings settings) {
			double minLose = settings.PeaceMinLoseBand;
			double minWin = settings.PeaceMinWinBand;
			double minPercent = settings.PeaceChanceMinPercent;
			double maxPercent = settings.PeaceChanceMaxPercent;

			if (minLose > 0) {
				double loseEdge = -100.0 + minLose;
				if (progress >= -100.0 && progress <= loseEdge) {
					double t = (loseEdge - progress) / minLose;
					return minPercent + t * (maxPercent - minPercent);
				}
			}

			if (minWin > 0) {
				double winEdge = 100.0 - minWin;
				if (progress >= winEdge && progress <= 100.0) {
					double t = (progress - winEdge) / minWin;
					return minPercent + t * (maxPercent - minPercent);
				}
			}

			return 0.0;
		}

		static int CalculateCapacity(
			IReadOnlyWorld world, string attackerCountryId, string defenderCountryId,
			ProvinceTopology topology, WarBattleSettings settings) {
			var attackerProvinces = new HashSet<string>(StringComparer.Ordinal);
			var defenderProvinces = new HashSet<string>(StringComparer.Ordinal);
			foreach (string provinceId in topology.ProvinceIds) {
				string ownerId = ProvinceOwnershipSystem.GetOwner(world, provinceId);
				if (ownerId == attackerCountryId) {
					attackerProvinces.Add(provinceId);
				} else if (ownerId == defenderCountryId) {
					defenderProvinces.Add(provinceId);
				}
			}

			int sharedPairs = 0;
			foreach (string provinceId in attackerProvinces) {
				foreach (string neighborId in topology.GetNeighbors(provinceId)) {
					if (defenderProvinces.Contains(neighborId)) {
						sharedPairs++;
					}
				}
			}
			return settings.BaseConcurrentBattleCount
				+ sharedPairs / settings.SharedBorderPairsPerAdditionalBattle;
		}

		static string? FindWarIdForCountry(IReadOnlyWorld world, string countryId) {
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].CountryId == countryId) {
						return participants[i].WarId;
					}
				}
			}
			return null;
		}

		static bool TryGetWarProgress(ResourceQuery resources, IReadOnlyWorld world, string warId, out double progress) {
			return resources.TryGetValue(world, warId, ResourceDefinitions.WarProgress, out progress);
		}

		static bool TryGetWarState(
			ResourceQuery resources,
			IReadOnlyWorld world,
			string warId,
			out string attackerId,
			out string defenderId,
			out double progress,
			out DateTime declaredAt) {
			attackerId = "";
			defenderId = "";
			progress = 0;
			declaredAt = default;

			bool foundWar = false;
			int[] warRequired = { TypeId<War>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = arch.GetColumn<War>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (wars[i].WarId == warId) {
						declaredAt = wars[i].DeclaredAt;
						foundWar = true;
						break;
					}
				}
				if (foundWar) {
					break;
				}
			}
			if (!foundWar || !resources.TryGetValue(world, warId, ResourceDefinitions.WarProgress, out progress)) {
				return false;
			}

			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (participants[i].WarId != warId) {
						continue;
					}
					if (participants[i].Kind == WarParticipantKind.Attacker) {
						attackerId = participants[i].CountryId;
					} else if (participants[i].Kind == WarParticipantKind.Defender) {
						defenderId = participants[i].CountryId;
					}
				}
			}

			return attackerId != "" && defenderId != "";
		}

		static void DestroyWar(World world, ResourceQuery resources, string warId) {
			var battleEntities = new List<int>();
			var forceEntities = new List<int>();
			foreach (WarBattles.BattleInfo battle in WarBattles.GetBattles(world, warId)) {
				battleEntities.Add(battle.Entity);
				foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battle.Value.BattleId)) {
					if (battle.Value.State == BattleState.Active && force.Value.Troops > 0) {
						ResourceMutations.TryApplyClampedDelta(
							resources, world, force.Value.CountryId, ResourceDefinitions.Recruits,
							force.Value.Troops, 0, double.MaxValue, out _);
					}
					forceEntities.Add(force.Entity);
				}
			}
			foreach (int entity in forceEntities) {
				world.Destroy(entity);
			}
			foreach (int entity in battleEntities) {
				world.Destroy(entity);
			}

			var matchingParticipants = new List<int>();
			int[] participantRequired = { TypeId<WarParticipant>.Value };
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

			resources.Remove(world, warId, ResourceDefinitions.WarProgress);

			var matchingWars = new List<int>();
			int[] warRequired = { TypeId<War>.Value };
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

		static void ClearOccupationForParticipants(World world, string countryIdA, string countryIdB) {
			int[] required = { TypeId<ProvinceOwnership>.Value };
			var provinceIds = new List<string>();
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string ownerId = ownerships[i].OwnerId;
					if (ownerId == countryIdA || ownerId == countryIdB) {
						provinceIds.Add(ownerships[i].ProvinceId);
					}
				}
			}
			foreach (string provinceId in provinceIds) {
				ProvinceOccupationSystem.ClearOccupier(world, provinceId);
			}
		}

		static List<WarProvinceTransferSnapshot> TransferOccupiedProvinces(
			World world,
			string winnerId,
			string loserId,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters) {
			var transferred = new List<WarProvinceTransferSnapshot>();
			var eligible = new List<string>();
			int[] ownershipRequired = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(ownershipRequired, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (ownerships[i].OwnerId != loserId) {
						continue;
					}
					string provinceId = ownerships[i].ProvinceId;
					string occupierId = ProvinceOccupationSystem.GetOccupier(world, provinceId);
					if (occupierId != "" && occupierId != loserId) {
						eligible.Add(provinceId);
					}
				}
			}

			if (eligible.Count == 0) {
				return transferred;
			}

			int minPercent = (int)Math.Round(settings.PeaceProvinceTransferMinPercent);
			int maxPercent = (int)Math.Round(settings.PeaceProvinceTransferMaxPercent);
			if (maxPercent < minPercent) {
				maxPercent = minPercent;
			}
			int percent = minPercent + rng.Next(maxPercent - minPercent + 1);
			double fraction = percent / 100.0;
			int transferCount = Math.Min(eligible.Count, (int)Math.Ceiling(eligible.Count * fraction));
			if (transferCount <= 0) {
				return transferred;
			}

			bool hasCentroid = TryComputeWinnerCentroid(
				world, winnerId, topology, provinceCenters, out double centroidLon, out double centroidLat);
			eligible.Sort((a, b) => {
				if (hasCentroid) {
					double da = DistanceSquared(a, centroidLon, centroidLat, provinceCenters);
					double db = DistanceSquared(b, centroidLon, centroidLat, provinceCenters);
					int cmp = da.CompareTo(db);
					if (cmp != 0) {
						return cmp;
					}
				}
				return string.CompareOrdinal(a, b);
			});

			for (int i = 0; i < transferCount; i++) {
				string provinceId = eligible[i];
				ProvinceOwnershipSystem.ChangeOwner(world, provinceId, winnerId);
				transferred.Add(new WarProvinceTransferSnapshot {
					ProvinceId = provinceId,
					OldOwnerCountryId = loserId,
					NewOwnerCountryId = winnerId
				});
			}
			return transferred;
		}

		// A winner's overseas colonies (owned provinces generated from the country's
		// secondaryMapFeatureIds) would otherwise pull the centroid away from its home territory,
		// making transferred provinces prefer proximity to a colony over the country the winner
		// actually lives in. Average only owned provinces flagged isMainTerritory (province_config.json,
		// derived from mainMapFeatureIds at generation time); fall back to every owned province if the
		// winner holds no main territory at all (e.g. it lost its homeland but kept colonies).
		static bool TryComputeWinnerCentroid(
			IReadOnlyWorld world,
			string winnerId,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			out double centroidLon,
			out double centroidLat) {
			centroidLon = 0;
			centroidLat = 0;

			var ownedProvinces = new List<string>();
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (ownerships[i].OwnerId == winnerId) {
						ownedProvinces.Add(ownerships[i].ProvinceId);
					}
				}
			}
			if (ownedProvinces.Count == 0) {
				return false;
			}

			bool anyMainTerritory = false;
			foreach (string provinceId in ownedProvinces) {
				if (topology.IsMainTerritory(provinceId)) {
					anyMainTerritory = true;
					break;
				}
			}

			double sumLon = 0;
			double sumLat = 0;
			int n = 0;
			foreach (string provinceId in ownedProvinces) {
				if (anyMainTerritory && !topology.IsMainTerritory(provinceId)) {
					continue;
				}
				if (provinceCenters.TryGetValue(provinceId, out var center)) {
					sumLon += center.Lon;
					sumLat += center.Lat;
					n++;
				}
			}
			if (n == 0) {
				return false;
			}
			centroidLon = sumLon / n;
			centroidLat = sumLat / n;
			return true;
		}

		static double DistanceSquared(
			string provinceId,
			double centroidLon,
			double centroidLat,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters) {
			if (!provinceCenters.TryGetValue(provinceId, out var center)) {
				return double.MaxValue;
			}
			double dLon = center.Lon - centroidLon;
			double dLat = center.Lat - centroidLat;
			return dLon * dLon + dLat * dLat;
		}

		static (double GoldTaken, List<WarGoldRecipientSnapshot> Recipients) TransferGoldSpoils(
			World world,
			ResourceQuery resources,
			string winnerId,
			string loserId,
			DateTime declaredAt,
			DateTime currentTime,
			GameSettings settings,
			int maxControlPool) {
			int durationMonths = ComputeBillableWarMonths(declaredAt, currentTime);
			double amount = durationMonths * settings.PeaceGoldPerMonth;
			if (amount == 0.0) {
				return (0.0, new List<WarGoldRecipientSnapshot>());
			}

			CollectGoldFromSide(world, resources, loserId, amount, maxControlPool);
			List<WarGoldRecipientSnapshot> recipients = PayoutGoldToSide(world, resources, winnerId, amount, maxControlPool);
			return (amount, recipients);
		}

		// Bill war length in 30-day months, rounding partial months up (2 days → 1, 32 days → 2).
		public static int ComputeBillableWarMonths(DateTime declaredAt, DateTime currentTime) {
			double totalDays = (currentTime - declaredAt).TotalDays;
			if (totalDays <= 0) {
				return 0;
			}
			return (int)Math.Ceiling(totalDays / 30.0);
		}

		static void CollectGoldFromSide(
			World world, ResourceQuery resources, string countryId, double amount, int maxControlPool) {
			if (amount == 0.0) {
				return;
			}
			int pool = Math.Max(1, maxControlPool);
			var orgTotals = GetOrgControlTotalsInCountry(world, countryId);

			double attributed = 0.0;
			foreach (var pair in orgTotals) {
				if (pair.Value <= 0) {
					continue;
				}
				double share = amount * (pair.Value / (double)pool);
				resources.ApplyDelta(world, pair.Key, ResourceDefinitions.Gold, -share, OwnerType.Org);
				attributed += share;
			}
			double remainder = amount - attributed;
			if (remainder != 0.0) {
				resources.ApplyDelta(world, countryId, ResourceDefinitions.Gold, -remainder, OwnerType.Country);
			}
		}

		static List<WarGoldRecipientSnapshot> PayoutGoldToSide(
			World world, ResourceQuery resources, string countryId, double amount, int maxControlPool) {
			var recipients = new List<WarGoldRecipientSnapshot>();
			if (amount == 0.0) {
				return recipients;
			}
			int pool = Math.Max(1, maxControlPool);
			var orgTotals = GetOrgControlTotalsInCountry(world, countryId);

			double attributed = 0.0;
			foreach (var pair in orgTotals) {
				if (pair.Value <= 0) {
					continue;
				}
				double share = amount * (pair.Value / (double)pool);
				resources.ApplyDelta(world, pair.Key, ResourceDefinitions.Gold, share, OwnerType.Org);
				recipients.Add(new WarGoldRecipientSnapshot {
					OwnerType = OwnerType.Org,
					OwnerId = pair.Key,
					Amount = share
				});
				attributed += share;
			}
			double remainder = amount - attributed;
			if (remainder != 0.0) {
				resources.ApplyDelta(world, countryId, ResourceDefinitions.Gold, remainder, OwnerType.Country);
				recipients.Add(new WarGoldRecipientSnapshot {
					OwnerType = OwnerType.Country,
					OwnerId = countryId,
					Amount = remainder
				});
			}
			return recipients;
		}

		static List<WarControlDeltaSnapshot> ApplyControlShifts(
			World world,
			string winnerId,
			string loserId,
			GameSettings settings,
			int maxControlPool) {
			var deltas = new List<WarControlDeltaSnapshot>();
			ApplyWinnerControlBoosts(
				world, winnerId, settings.PeaceWinnerControlIncreaseFraction, maxControlPool, deltas);
			ApplyLoserControlCuts(
				world, loserId, settings.PeaceLoserControlDecreaseFraction, deltas);
			return deltas;
		}

		static void ApplyWinnerControlBoosts(
			World world,
			string countryId,
			double fraction,
			int maxControlPool,
			List<WarControlDeltaSnapshot> deltas) {
			var totals = GetOrgControlTotalsInCountry(world, countryId);
			if (totals.Count == 0) {
				return;
			}

			var ordered = new List<(string OrgId, int Total)>();
			foreach (var pair in totals) {
				if (pair.Value > 0) {
					ordered.Add((pair.Key, pair.Value));
				}
			}
			ordered.Sort((a, b) => {
				int cmp = b.Total.CompareTo(a.Total);
				return cmp != 0 ? cmp : string.CompareOrdinal(a.OrgId, b.OrgId);
			});

			foreach (var (orgId, orgTotal) in ordered) {
				int desired = (int)Math.Round(orgTotal * fraction, MidpointRounding.AwayFromZero);
				if (desired <= 0) {
					continue;
				}
				int liveTotal = ControlQuery.GetTotalControlInCountry(world, countryId);
				int room = maxControlPool - liveTotal;
				int orgRoom = 100 - orgTotal;
				int delta = Math.Min(desired, Math.Min(room, orgRoom));
				if (delta <= 0) {
					continue;
				}
				ControlSystem.ApplyChangeControl(world, orgId, countryId, delta, maxControlPool);
				deltas.Add(new WarControlDeltaSnapshot {
					CountryId = countryId,
					OrgId = orgId,
					Delta = delta,
					TotalAfter = ControlQuery.GetOrgControlInCountry(world, orgId, countryId)
				});
			}
		}

		static void ApplyLoserControlCuts(
			World world,
			string countryId,
			double fraction,
			List<WarControlDeltaSnapshot> deltas) {
			var totals = GetOrgControlTotalsInCountry(world, countryId);
			if (totals.Count == 0) {
				return;
			}

			var ordered = new List<(string OrgId, int Total)>();
			foreach (var pair in totals) {
				if (pair.Value > 0) {
					ordered.Add((pair.Key, pair.Value));
				}
			}
			ordered.Sort((a, b) => {
				int cmp = b.Total.CompareTo(a.Total);
				return cmp != 0 ? cmp : string.CompareOrdinal(a.OrgId, b.OrgId);
			});

			foreach (var (orgId, orgTotal) in ordered) {
				int desired = (int)Math.Round(orgTotal * fraction, MidpointRounding.AwayFromZero);
				if (desired <= 0) {
					continue;
				}
				int cut = Math.Min(desired, orgTotal);
				if (cut <= 0) {
					continue;
				}
				ControlQuery.ReduceOrgControlInCountry(world, orgId, countryId, cut);
				deltas.Add(new WarControlDeltaSnapshot {
					CountryId = countryId,
					OrgId = orgId,
					Delta = -cut,
					TotalAfter = ControlQuery.GetOrgControlInCountry(world, orgId, countryId)
				});
			}
		}

		static Dictionary<string, int> GetOrgControlTotalsInCountry(IReadOnlyWorld world, string countryId) {
			var totals = new Dictionary<string, int>(StringComparer.Ordinal);
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].CountryId != countryId || controls[i].Value == 0) {
						continue;
					}
					totals.TryGetValue(controls[i].OrgId, out int total);
					totals[controls[i].OrgId] = total + controls[i].Value;
				}
			}
			return totals;
		}
	}
}
