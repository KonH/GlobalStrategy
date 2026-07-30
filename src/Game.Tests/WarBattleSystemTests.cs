using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarBattleSystemTests {
		static readonly DateTime Start = new DateTime(1880, 1, 1);

		static ProvinceConfig BuildProvinceConfig() {
			return new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry {
						ProvinceId = "France__west",
						CountryId = "France",
						CentroidX = 0,
						CentroidY = 0,
						NeighborProvinceIds = new List<string> { "Germany__east" }
					},
					new ProvinceEntry {
						ProvinceId = "Germany__east",
						CountryId = "Germany",
						CentroidX = 1,
						CentroidY = 0,
						NeighborProvinceIds = new List<string> { "France__west" }
					}
				}
			};
		}

		static World BuildWorld(double attackerRecruits, double defenderRecruits) {
			var world = new World();
			var provinceConfig = BuildProvinceConfig();
			ProvinceOwnershipSystem.Seed(world, provinceConfig);
			ProvinceOccupationSystem.Seed(world, provinceConfig);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Recruits, attackerRecruits);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.WarInitiative, 0);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Damage, 300);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Durability, 300);
			AddResource(world, "Germany", OwnerType.Country, ResourceDefinitions.Recruits, defenderRecruits);
			AddResource(world, "Germany", OwnerType.Country, ResourceDefinitions.WarInitiative, 0);
			AddResource(world, "Germany", OwnerType.Country, ResourceDefinitions.Damage, 300);
			AddResource(world, "Germany", OwnerType.Country, ResourceDefinitions.Durability, 300);
			AddResource(world, "France__west", OwnerType.Province, ResourceDefinitions.Population, 1000);
			AddResource(world, "Germany__east", OwnerType.Province, ResourceDefinitions.Population, 1000);
			return world;
		}

		static void AddResource(World world, string ownerId, OwnerType ownerType, string resourceId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(ownerId, ownerType));
			world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
		}

		static T GetSingle<T>(World world) {
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<T>()[0];
				}
			}
			throw new InvalidOperationException($"No {typeof(T).Name} entity found.");
		}

		static int Count<T>(World world) {
			int result = 0;
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				result += arch.Count;
			}
			return result;
		}

		[Fact]
		void declaration_captures_border_capacity_and_initializes_initiative() {
			var world = BuildWorld(100, 100);
			var topology = new ProvinceTopology(BuildProvinceConfig());
			var settings = new WarBattleSettings();

			Assert.True(Wars.DeclareWar(world, "France", "Germany", Start, topology, settings));

			Assert.Equal(1, GetSingle<WarBattleCapacity>(world).MaxConcurrentBattleCount);
			Assert.Equal(1, ResourceQuery.GetValue(world, "France", ResourceDefinitions.WarInitiative));
			Assert.Equal(0, ResourceQuery.GetValue(world, "Germany", ResourceDefinitions.WarInitiative));
		}

		[Fact]
		void elapsed_hour_creates_forces_and_finishes_zero_troop_defender() {
			var world = BuildWorld(100, 0);
			var topology = new ProvinceTopology(BuildProvinceConfig());
			var settings = new WarBattleSettings();
			Wars.DeclareWar(world, "France", "Germany", Start, topology, settings);

			WarBattleSystem.Update(world, Start, Start.AddHours(1), new Random(7), topology, settings);

			Assert.Equal(1, Count<Battle>(world));
			Assert.Equal(2, Count<BattleForce>(world));
			Battle battle = GetSingle<Battle>(world);
			Assert.Equal(BattleState.Finished, battle.State);
			Assert.Equal(WarParticipantKind.Attacker, battle.Winner);
			Assert.Equal(10, GetSingle<WarProgress>(world).Value);
			Assert.Equal("France", ProvinceOccupationSystem.GetOccupier(world, "Germany__east"));
		}
	}
}
