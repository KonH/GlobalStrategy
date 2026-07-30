using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DamageDurabilityResourcesTests {
		const string Britain = "Great_Britain";
		const string France = "France";
		const int BritainBaseDamage = 70;
		const int BritainBaseDurability = 65;
		const int FranceBaseDamage = 80;
		const int FranceBaseDurability = 75;

		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static SkillSettings Fixed(int value) {
			return new SkillSettings { MinValue = value, MaxValue = value };
		}

		static CharacterEntry Entry(string characterId, Dictionary<string, SkillSettings> skills) {
			return new CharacterEntry {
				CharacterId = characterId,
				NamePartKeys = new List<string> { "character.name.test" },
				Skills = skills
			};
		}

		static Dictionary<string, List<CharacterEntry>> BuildSlots(string countryPrefix) {
			return new Dictionary<string, List<CharacterEntry>> {
				["ruler"] = new List<CharacterEntry> {
					Entry($"{countryPrefix}_ruler_1", new Dictionary<string, SkillSettings> {
						["power"] = Fixed(20),
						["stinginess"] = Fixed(15)
					}),
					Entry($"{countryPrefix}_ruler_2", new Dictionary<string, SkillSettings> {
						["power"] = Fixed(30),
						["stinginess"] = Fixed(25)
					})
				},
				["military_advisor"] = new List<CharacterEntry> {
					Entry($"{countryPrefix}_mil_1", new Dictionary<string, SkillSettings> {
						["power"] = Fixed(10)
					}),
					Entry($"{countryPrefix}_mil_2", new Dictionary<string, SkillSettings> {
						["power"] = Fixed(50)
					})
				},
				["economic_advisor"] = new List<CharacterEntry> {
					Entry($"{countryPrefix}_eco_1", new Dictionary<string, SkillSettings> {
						["stinginess"] = Fixed(12)
					}),
					Entry($"{countryPrefix}_eco_2", new Dictionary<string, SkillSettings> {
						["stinginess"] = Fixed(40)
					})
				}
			};
		}

		static CharacterConfig BuildCharacterConfig() {
			return new CharacterConfig {
				Skills = new List<CharacterSkillDefinition> {
					new CharacterSkillDefinition { SkillId = "power" },
					new CharacterSkillDefinition { SkillId = "stinginess" }
				},
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition {
						RoleId = "ruler",
						SkillIds = new List<string> { "power", "stinginess" }
					},
					new CharacterRoleDefinition {
						RoleId = "military_advisor",
						SkillIds = new List<string> { "power" }
					},
					new CharacterRoleDefinition {
						RoleId = "economic_advisor",
						SkillIds = new List<string> { "stinginess" }
					}
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool { CountryId = Britain, Slots = BuildSlots("gb") },
					new CountryCharacterPool { CountryId = France, Slots = BuildSlots("fr") }
				}
			};
		}

		static ResourceConfig BuildResourceConfig() {
			return new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition { ResourceId = "power", SeedTarget = ResourceSeedTarget.Character },
					new ResourceDefinition { ResourceId = "stinginess", SeedTarget = ResourceSeedTarget.Character },
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Damage,
						SeedTarget = ResourceSeedTarget.Country,
						DefaultInitialValue = 0.0
					},
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Durability,
						SeedTarget = ResourceSeedTarget.Country,
						DefaultInitialValue = 0.0
					}
				}
			};
		}

		static CountryConfig BuildCountryConfig() {
			return new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = Britain,
						DisplayName = "Great Britain",
						IsAvailable = true,
						BaseDamage = BritainBaseDamage,
						BaseDurability = BritainBaseDurability
					},
					new CountryEntry {
						CountryId = France,
						DisplayName = "France",
						IsAvailable = true,
						BaseDamage = FranceBaseDamage,
						BaseDurability = FranceBaseDurability
					}
				}
			};
		}

		static GameLogic BuildLogic() {
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = Britain,
						InitialGold = 1000.0
					}
				}
			};
			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(BuildCountryConfig()),
				new StaticConfig<GameSettings>(new GameSettings()),
				new StaticConfig<ResourceConfig>(BuildResourceConfig()),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				character: new StaticConfig<CharacterConfig>(BuildCharacterConfig()),
				province: new StaticConfig<ProvinceConfig>(new ProvinceConfig()),
				rngSeed: 42);
			return new GameLogic(ctx);
		}

		static List<(string OwnerId, OwnerType OwnerType, string ResourceId, PayType PayType, string CollectorId)> GetCollectors(World world) {
			var result = new List<(string, OwnerType, string, PayType, string)>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value,
				TypeId<ResourceCollector>.Value
			};
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				ResourceLink[] links = archetype.GetColumn<ResourceLink>();
				ResourceEffect[] effects = archetype.GetColumn<ResourceEffect>();
				ResourceCollector[] collectors = archetype.GetColumn<ResourceCollector>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add((owners[i].OwnerId, owners[i].OwnerType, links[i].ResourceId,
						effects[i].PayType, collectors[i].CollectorId));
				}
			}
			return result;
		}

		static double ExpectedDamage(IReadOnlyWorld world, string countryId, int baseDamage) {
			return baseDamage
				+ WartimeSkillQuery.GetSkill(world, countryId, "ruler", "power")
				+ WartimeSkillQuery.GetSkill(world, countryId, "military_advisor", "power");
		}

		static double ExpectedDurability(IReadOnlyWorld world, string countryId, int baseDurability) {
			return baseDurability
				+ WartimeSkillQuery.GetSkill(world, countryId, "ruler", "stinginess")
				+ WartimeSkillQuery.GetSkill(world, countryId, "economic_advisor", "stinginess");
		}

		static bool HasCountryResource(World world, string countryId, string resourceId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					if (owners[i].OwnerId == countryId
						&& owners[i].OwnerType == OwnerType.Country
						&& resources[i].ResourceId == resourceId) {
						return true;
					}
				}
			}
			return false;
		}

		[Fact]
		void init_creates_damage_and_durability_with_instant_and_daily_collectors() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.True(HasCountryResource(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.True(HasCountryResource(logic.World, Britain, ResourceDefinitions.Durability));
			Assert.True(HasCountryResource(logic.World, France, ResourceDefinitions.Damage));
			Assert.True(HasCountryResource(logic.World, France, ResourceDefinitions.Durability));

			var collectors = GetCollectors(logic.World)
				.Where(c => c.OwnerType == OwnerType.Country
					&& (c.ResourceId == ResourceDefinitions.Damage || c.ResourceId == ResourceDefinitions.Durability))
				.ToList();
			Assert.Contains(collectors, c =>
				c.OwnerId == Britain && c.ResourceId == ResourceDefinitions.Damage
				&& c.PayType == PayType.Daily && c.CollectorId == DamageCollector.Id);
			Assert.Contains(collectors, c =>
				c.OwnerId == Britain && c.ResourceId == ResourceDefinitions.Durability
				&& c.PayType == PayType.Daily && c.CollectorId == DurabilityCollector.Id);
			Assert.Equal(
				ExpectedDamage(logic.World, Britain, BritainBaseDamage),
				ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(logic.World, Britain, BritainBaseDurability),
				ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Durability));
		}

		[Fact]
		void stop_war_preserves_damage_and_durability() {
			var logic = BuildLogic();
			logic.Update(0f);
			logic.Commands.Push(new DebugDeclareWarCommand {
				AttackerCountryId = Britain,
				DefenderCountryId = France
			});
			logic.Update(0f);

			double britainDamage = ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Damage);
			double britainDurability = ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Durability);

			logic.Commands.Push(new DebugStopWarCommand { CountryId = Britain });
			logic.Update(0f);

			Assert.False(Wars.IsInWar(logic.World, Britain));
			Assert.True(HasCountryResource(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.True(HasCountryResource(logic.World, Britain, ResourceDefinitions.Durability));
			Assert.Equal(britainDamage, ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(britainDurability, ResourceQuery.GetValue(logic.World, Britain, ResourceDefinitions.Durability));
		}

		[Fact]
		void declare_war_does_not_duplicate_damage_or_durability() {
			var logic = BuildLogic();
			logic.Update(0f);

			int resourceCountBefore = CountCombatResources(logic.World);
			logic.Commands.Push(new DebugDeclareWarCommand {
				AttackerCountryId = Britain,
				DefenderCountryId = France
			});
			logic.Update(0f);

			Assert.Equal(resourceCountBefore, CountCombatResources(logic.World));
		}

		static int CountCombatResources(World world) {
			int count = 0;
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					if (owners[i].OwnerType != OwnerType.Country) {
						continue;
					}
					if (resources[i].ResourceId == ResourceDefinitions.Damage
						|| resources[i].ResourceId == ResourceDefinitions.Durability) {
						count++;
					}
				}
			}
			return count;
		}
	}
}
