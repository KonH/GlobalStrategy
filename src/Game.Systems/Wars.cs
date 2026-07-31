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

		public static bool DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime) {
			return DeclareWar(
				world, attackerCountryId, defenderCountryId, currentTime,
				new ProvinceTopology(new ProvinceConfig()), new WarBattleSettings());
		}

		public static bool DeclareWar(
			World world, string attackerCountryId, string defenderCountryId, DateTime currentTime,
			ProvinceTopology topology, WarBattleSettings settings) {
			if (attackerCountryId == defenderCountryId) {
				return false;
			}
			if (IsInWar(world, attackerCountryId) || IsInWar(world, defenderCountryId)) {
				return false;
			}

			string warId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}";

			int warEntity = world.Create();
			world.Add(warEntity, new War { WarId = warId, DeclaredAt = currentTime });
			world.Add(warEntity, new WarBattleCapacity {
				MaxConcurrentBattleCount = CalculateCapacity(
					world, attackerCountryId, defenderCountryId, topology, settings)
			});

			int progressEntity = world.Create();
			world.Add(progressEntity, new ResourceOwner(warId, OwnerType.War));
			world.Add(progressEntity, new Resource {
				ResourceId = ResourceDefinitions.WarProgress,
				Value = 0
			});
			world.Add(progressEntity, new ResourceHistory {
				History = new List<ResourceChangeEntry>()
			});

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
				world, attackerCountryId, ResourceDefinitions.WarInitiative,
				settings.AttackerInitialInitiative, out _);
			ResourceMutations.TrySetValue(
				world, defenderCountryId, ResourceDefinitions.WarInitiative,
				settings.DefenderInitialInitiative, out _);

			return true;
		}

		public static bool StopWar(
			World world,
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
				world, warId, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
			return true;
		}

		public static void TryResolvePeaceByChance(
			World world,
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
				if (!TryGetWarProgress(world, warId, out double progress)) {
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
				if (TryGetWarProgress(world, warId, out _)) {
					ResolvePeace(
						world, warId, currentTime, rng, settings, topology, provinceCenters, maxControlPool, countryConfig);
				}
			}
		}

		public static void ResolvePeace(
			World world,
			string warId,
			DateTime currentTime,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters,
			int maxControlPool,
			CountryConfig? countryConfig = null) {
			if (!TryGetWarState(world, warId, out string attackerId, out string defenderId, out double progress, out DateTime declaredAt)) {
				return;
			}

			if (progress == 0.0) {
				ClearOccupationForParticipants(world, attackerId, defenderId);
				DestroyWar(world, warId);
				return;
			}

			string winnerId = progress > 0.0 ? attackerId : defenderId;
			string loserId = progress > 0.0 ? defenderId : attackerId;

			List<WarProgressHistorySnapshot> history = WarProgressSnapshot.BuildHistory(world, warId);
			WarSideStatsSnapshot attackerStats = WarProgressSnapshot.BuildSideStats(
				world, warId, attackerId, WarParticipantKind.Attacker, countryConfig);
			WarSideStatsSnapshot defenderStats = WarProgressSnapshot.BuildSideStats(
				world, warId, defenderId, WarParticipantKind.Defender, countryConfig);
			List<WarBattleRowSnapshot> battles = WarProgressSnapshot.BuildBattles(world, warId);

			List<string> transferredProvinces = TransferOccupiedProvinces(
				world, winnerId, loserId, rng, settings, topology, provinceCenters);
			ClearOccupationForParticipants(world, attackerId, defenderId);
			(double goldTaken, List<WarGoldRecipientSnapshot> goldRecipients) = TransferGoldSpoils(
				world, winnerId, loserId, declaredAt, currentTime, settings);
			List<WarControlDeltaSnapshot> controlDeltas = ApplyControlShifts(
				world, winnerId, loserId, settings, maxControlPool);
			DestroyWar(world, warId);

			int ge = world.Create();
			world.Add(ge, new WarResolvedApplied {
				WarId = warId,
				AttackerCountryId = attackerId,
				DefenderCountryId = defenderId,
				WinnerCountryId = winnerId,
				LoserCountryId = loserId,
				Progress = progress,
				GoldTaken = goldTaken,
				GoldRecipients = goldRecipients,
				ControlDeltas = controlDeltas,
				TransferredProvinceIds = transferredProvinces,
				History = history,
				Attacker = attackerStats,
				Defender = defenderStats,
				Battles = battles
			});
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

		static bool TryGetWarProgress(IReadOnlyWorld world, string warId, out double progress) {
			return ResourceQuery.TryGetValue(world, warId, ResourceDefinitions.WarProgress, out progress);
		}

		static bool TryGetWarState(
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
			if (!foundWar || !ResourceQuery.TryGetValue(world, warId, ResourceDefinitions.WarProgress, out progress)) {
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

		static void DestroyWar(World world, string warId) {
			var battleEntities = new List<int>();
			var forceEntities = new List<int>();
			foreach (WarBattles.BattleInfo battle in WarBattles.GetBattles(world, warId)) {
				battleEntities.Add(battle.Entity);
				foreach (WarBattles.ForceInfo force in WarBattles.GetForces(world, battle.Value.BattleId)) {
					if (battle.Value.State == BattleState.Active && force.Value.Troops > 0) {
						ResourceMutations.TryApplyClampedDelta(
							world, force.Value.CountryId, ResourceDefinitions.Recruits,
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

			int[] resourceRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			var matchingResources = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == warId
						&& resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						matchingResources.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in matchingResources) {
				world.Destroy(e);
			}

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

		static List<string> TransferOccupiedProvinces(
			World world,
			string winnerId,
			string loserId,
			Random rng,
			GameSettings settings,
			ProvinceTopology topology,
			IReadOnlyDictionary<string, (double Lon, double Lat)> provinceCenters) {
			var transferred = new List<string>();
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
				ProvinceOwnershipSystem.ChangeOwner(world, eligible[i], winnerId);
				transferred.Add(eligible[i]);
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
			string winnerId,
			string loserId,
			DateTime declaredAt,
			DateTime currentTime,
			GameSettings settings) {
			int durationMonths = (currentTime.Year - declaredAt.Year) * 12 + (currentTime.Month - declaredAt.Month);
			if (durationMonths < 0) {
				durationMonths = 0;
			}
			double amount = durationMonths * settings.PeaceGoldPerMonth;
			if (amount == 0.0) {
				return (0.0, new List<WarGoldRecipientSnapshot>());
			}

			CollectGoldFromSide(world, loserId, amount);
			List<WarGoldRecipientSnapshot> recipients = PayoutGoldToSide(world, winnerId, amount);
			return (amount, recipients);
		}

		static void CollectGoldFromSide(World world, string countryId, double amount) {
			if (amount == 0.0) {
				return;
			}
			var orgTotals = GetOrgControlTotalsInCountry(world, countryId);
			int totalControl = 0;
			foreach (var pair in orgTotals) {
				totalControl += pair.Value;
			}

			double attributed = 0.0;
			if (totalControl > 0) {
				foreach (var pair in orgTotals) {
					if (pair.Value <= 0) {
						continue;
					}
					double share = amount * (pair.Value / (double)totalControl);
					AdjustGold(world, pair.Key, OwnerType.Org, -share);
					attributed += share;
				}
			}
			double remainder = amount - attributed;
			if (remainder != 0.0) {
				AdjustGold(world, countryId, OwnerType.Country, -remainder);
			}
		}

		static List<WarGoldRecipientSnapshot> PayoutGoldToSide(World world, string countryId, double amount) {
			var recipients = new List<WarGoldRecipientSnapshot>();
			if (amount == 0.0) {
				return recipients;
			}
			var orgTotals = GetOrgControlTotalsInCountry(world, countryId);
			int totalControl = 0;
			foreach (var pair in orgTotals) {
				totalControl += pair.Value;
			}

			double attributed = 0.0;
			if (totalControl > 0) {
				foreach (var pair in orgTotals) {
					if (pair.Value <= 0) {
						continue;
					}
					double share = amount * (pair.Value / (double)totalControl);
					AdjustGold(world, pair.Key, OwnerType.Org, share);
					recipients.Add(new WarGoldRecipientSnapshot {
						OwnerType = OwnerType.Org,
						OwnerId = pair.Key,
						Amount = share
					});
					attributed += share;
				}
			}
			double remainder = amount - attributed;
			if (remainder != 0.0) {
				AdjustGold(world, countryId, OwnerType.Country, remainder);
				recipients.Add(new WarGoldRecipientSnapshot {
					OwnerType = OwnerType.Country,
					OwnerId = countryId,
					Amount = remainder
				});
			}
			return recipients;
		}

		static void AdjustGold(World world, string ownerId, OwnerType ownerType, double delta) {
			if (delta == 0.0) {
				return;
			}
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId
						&& owners[i].OwnerType == ownerType
						&& resources[i].ResourceId == ResourceDefinitions.Gold) {
						resources[i].Value += delta;
						return;
					}
				}
			}
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(ownerId, ownerType));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.Gold, Value = delta });
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
