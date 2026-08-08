using System;
using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DamageDurabilityGameLogicTests {
		const string Britain = "Great_Britain";
		const string France = "France";
		const int BritainBaseDamage = 70;
		const int BritainBaseDurability = 65;
		const int FranceBaseDamage = 80;
		const int FranceBaseDurability = 75;

		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		sealed class InMemoryStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new Dictionary<string, string>();
			public void Write(string path, string content) => _files[path] = content;
			public string Read(string path) => _files[path];
			public bool Exists(string path) => _files.ContainsKey(path);
			public void Delete(string path) => _files.Remove(path);
			public IReadOnlyList<string> List(string dir) {
				var result = new List<string>();
				string prefix = dir + "/";
				foreach (var key in _files.Keys) {
					if (key.StartsWith(prefix)) {
						result.Add(key.Substring(prefix.Length));
					}
				}
				return result;
			}
		}

		sealed class PassthroughSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _store = new Dictionary<string, WorldSnapshot>();
			public string LastSaveName { get; private set; } = "";

			public string Serialize(WorldSnapshot snapshot) {
				LastSaveName = snapshot.Header.SaveName;
				_store[snapshot.Header.SaveName] = snapshot;
				return snapshot.Header.SaveName;
			}

			public WorldSnapshot Deserialize(string json) => _store[json];
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

		static GameLogic BuildLogic(
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null) {
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
				storage: storage,
				serializer: serializer,
				initialOrganizationId: "Illuminati",
				character: new StaticConfig<CharacterConfig>(BuildCharacterConfig()),
				province: new StaticConfig<ProvinceConfig>(new ProvinceConfig()),
				rngSeed: 42);
			return new GameLogic(ctx);
		}

		static double ExpectedDamage(IReadOnlyWorld world, string countryId, int baseDamage, ResourceQuery resources) {
			return baseDamage
				+ WartimeSkillQuery.GetSkill(world, countryId, "ruler", "power", resources)
				+ WartimeSkillQuery.GetSkill(world, countryId, "military_advisor", "power", resources);
		}

		static double ExpectedDurability(IReadOnlyWorld world, string countryId, int baseDurability, ResourceQuery resources) {
			return baseDurability
				+ WartimeSkillQuery.GetSkill(world, countryId, "ruler", "stinginess", resources)
				+ WartimeSkillQuery.GetSkill(world, countryId, "economic_advisor", "stinginess", resources);
		}

		static bool HasDailyCombatCollector(World world, string countryId, string resourceId, string collectorId) {
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
					if (owners[i].OwnerId == countryId
						&& owners[i].OwnerType == OwnerType.Country
						&& links[i].ResourceId == resourceId
						&& effects[i].PayType == PayType.Daily
						&& collectors[i].CollectorId == collectorId) {
						return true;
					}
				}
			}
			return false;
		}

		static void EnsureMilitaryPower(GameLogic logic, string countryId, double desiredPower) {
			for (int i = 0; i < 2; i++) {
				double power = WartimeSkillQuery.GetSkill(logic.World, countryId, "military_advisor", "power", logic.Resources);
				if (Math.Abs(power - desiredPower) < 0.001) {
					return;
				}
				logic.Commands.Push(new DebugCycleCharacterCommand {
					OwnerId = countryId,
					RoleId = "military_advisor",
					SlotIndex = 0
				});
				logic.Update(0f);
			}
			Assert.Fail($"Could not reach military_advisor power {desiredPower} for {countryId}");
		}

		[Fact]
		void peacetime_countries_have_settled_damage_and_durability_after_init() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(
				ExpectedDamage(logic.World, Britain, BritainBaseDamage, logic.Resources),
				logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(logic.World, Britain, BritainBaseDurability, logic.Resources),
				logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Durability));
			Assert.Equal(
				ExpectedDamage(logic.World, France, FranceBaseDamage, logic.Resources),
				logic.Resources.GetValue(logic.World, France, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(logic.World, France, FranceBaseDurability, logic.Resources),
				logic.Resources.GetValue(logic.World, France, ResourceDefinitions.Durability));
		}

		[Fact]
		void cycle_military_advisor_updates_damage_only() {
			var logic = BuildLogic();
			logic.Update(0f);
			EnsureMilitaryPower(logic, Britain, 10);

			double damageBefore = logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage);
			double durabilityBefore = logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Durability);
			Assert.Equal(ExpectedDamage(logic.World, Britain, BritainBaseDamage, logic.Resources), damageBefore);

			logic.Commands.Push(new DebugCycleCharacterCommand {
				OwnerId = Britain,
				RoleId = "military_advisor",
				SlotIndex = 0
			});
			logic.Update(0f);

			Assert.Equal(50, WartimeSkillQuery.GetSkill(logic.World, Britain, "military_advisor", "power", logic.Resources));
			Assert.Equal(damageBefore + 40, logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(durabilityBefore, logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Durability));
			Assert.Equal(
				ExpectedDamage(logic.World, Britain, BritainBaseDamage, logic.Resources),
				logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(logic.World, Britain, BritainBaseDurability, logic.Resources),
				logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Durability));
		}

		[Fact]
		void drop_military_advisor_removes_that_skill_from_damage() {
			var logic = BuildLogic();
			logic.Update(0f);

			double militaryPower = WartimeSkillQuery.GetSkill(logic.World, Britain, "military_advisor", "power", logic.Resources);
			double damageBefore = logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage);
			Assert.True(militaryPower > 0);

			logic.Commands.Push(new DebugDropCharacterCommand {
				OwnerId = Britain,
				RoleId = "military_advisor",
				SlotIndex = 0
			});
			logic.Update(0f);

			Assert.Equal(0, WartimeSkillQuery.GetSkill(logic.World, Britain, "military_advisor", "power", logic.Resources));
			Assert.Equal(damageBefore - militaryPower, logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDamage(logic.World, Britain, BritainBaseDamage, logic.Resources),
				logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage));
		}

		[Fact]
		void save_load_preserves_values_and_daily_effects() {
			var storage = new InMemoryStorage();
			var serializer = new PassthroughSerializer();
			var logic = BuildLogic(storage, serializer);
			logic.Update(0f);

			double britainDamage = logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Damage);
			double britainDurability = logic.Resources.GetValue(logic.World, Britain, ResourceDefinitions.Durability);
			Assert.Equal(ExpectedDamage(logic.World, Britain, BritainBaseDamage, logic.Resources), britainDamage);
			Assert.Equal(ExpectedDurability(logic.World, Britain, BritainBaseDurability, logic.Resources), britainDurability);
			Assert.True(HasDailyCombatCollector(logic.World, Britain, ResourceDefinitions.Damage, DamageCollector.Id));
			Assert.True(HasDailyCombatCollector(logic.World, Britain, ResourceDefinitions.Durability, DurabilityCollector.Id));

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loaded = BuildLogic(storage, serializer);
			loaded.LoadState(saveName);

			Assert.Equal(
				ExpectedDamage(loaded.World, Britain, BritainBaseDamage, loaded.Resources),
				loaded.Resources.GetValue(loaded.World, Britain, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(loaded.World, Britain, BritainBaseDurability, loaded.Resources),
				loaded.Resources.GetValue(loaded.World, Britain, ResourceDefinitions.Durability));
			Assert.Equal(
				ExpectedDamage(loaded.World, France, FranceBaseDamage, loaded.Resources),
				loaded.Resources.GetValue(loaded.World, France, ResourceDefinitions.Damage));
			Assert.Equal(
				ExpectedDurability(loaded.World, France, FranceBaseDurability, loaded.Resources),
				loaded.Resources.GetValue(loaded.World, France, ResourceDefinitions.Durability));
			Assert.True(HasDailyCombatCollector(loaded.World, Britain, ResourceDefinitions.Damage, DamageCollector.Id));
			Assert.True(HasDailyCombatCollector(loaded.World, Britain, ResourceDefinitions.Durability, DurabilityCollector.Id));
			Assert.True(HasDailyCombatCollector(loaded.World, France, ResourceDefinitions.Damage, DamageCollector.Id));
			Assert.True(HasDailyCombatCollector(loaded.World, France, ResourceDefinitions.Durability, DurabilityCollector.Id));
		}
	}
}
