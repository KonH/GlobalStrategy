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
			world.Add(warEntity, new War { WarId = warId });
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

		public static bool StopWar(World world, string countryId) {
			string? warId = null;
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].CountryId == countryId) {
						warId = participants[i].WarId;
						break;
					}
				}
				if (warId != null) { break; }
			}
			if (warId == null) {
				return false;
			}

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

			int[] warRequired = { TypeId<War>.Value };
			var matchingWars = new List<int>();
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

			return true;
		}
	}
}
