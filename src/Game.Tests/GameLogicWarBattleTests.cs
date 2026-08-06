using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class GameLogicWarBattleTests {
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		[Fact]
		void declare_command_creates_and_processes_a_battle_on_the_same_hour_boundary() {
			var countries = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "France",
						IsAvailable = true,
						BaseDamage = 300,
						BaseDurability = 300
					},
					new CountryEntry {
						CountryId = "Germany",
						IsAvailable = true,
						BaseDamage = 300,
						BaseDurability = 300
					}
				}
			};
			var provinces = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry {
						ProvinceId = "France__west",
						CountryId = "France",
						Population = 1000,
						NeighborProvinceIds = new List<string> { "Germany__east" }
					},
					new ProvinceEntry {
						ProvinceId = "Germany__east",
						CountryId = "Germany",
						Population = 1000,
						CentroidX = 1,
						NeighborProvinceIds = new List<string> { "France__west" }
					}
				}
			};
			var resources = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Population,
						SeedTarget = ResourceSeedTarget.Province
					},
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryPopulation },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Recruits },
					new ResourceDefinition { ResourceId = ResourceDefinitions.WarInitiative },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Damage },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Durability }
				}
			};
			var organizations = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						HqCountryId = "France"
					}
				}
			};
			var settings = new GameSettings {
				SpeedMultipliers = new[] { 1 },
				ResourceIdUpdateOrder = new[] {
					ResourceDefinitions.Population,
					ResourceDefinitions.CountryPopulation,
					ResourceDefinitions.Recruits,
					ResourceDefinitions.Damage,
					ResourceDefinitions.Durability
				}
			};
			var context = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countries),
				new StaticConfig<GameSettings>(settings),
				new StaticConfig<ResourceConfig>(resources),
				new StaticConfig<OrganizationConfig>(organizations),
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(provinces),
				rngSeed: 11);
			var logic = new GameLogic(context);
			logic.Update(0);

			logic.Commands.Push(new DebugDeclareWarCommand {
				AttackerCountryId = "France",
				DefenderCountryId = "Germany"
			});
			logic.Update(1);

			Assert.Equal(1, Count<Battle>(logic.World));
			Assert.Equal(2, Count<BattleForce>(logic.World));
			Assert.True(GetTotalCasualties(logic.World) > 0);
		}

		static int Count<T>(World world) {
			int result = 0;
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				result += arch.Count;
			}
			return result;
		}

		static T GetSingle<T>(World world) {
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<T>()[0];
				}
			}
			throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} entity found.");
		}

		static double GetTotalCasualties(World world) {
			double result = 0;
			int[] required = { TypeId<BattleForce>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				BattleForce[] forces = arch.GetColumn<BattleForce>();
				for (int i = 0; i < arch.Count; i++) {
					result += forces[i].Casualties;
				}
			}
			return result;
		}
	}
}
