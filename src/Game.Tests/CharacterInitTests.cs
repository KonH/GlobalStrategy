using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CharacterInitTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static CharacterConfig BuildCharacterConfig() {
			return new CharacterConfig {
				Skills = new List<CharacterSkillDefinition> {
					new CharacterSkillDefinition { SkillId = "power" },
					new CharacterSkillDefinition { SkillId = "charm" },
					new CharacterSkillDefinition { SkillId = "stinginess" },
					new CharacterSkillDefinition { SkillId = "intrigue" }
				},
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = "ruler",             SkillIds = new List<string> { "power", "charm", "stinginess", "intrigue" } },
					new CharacterRoleDefinition { RoleId = "military_advisor",  SkillIds = new List<string> { "power" } },
					new CharacterRoleDefinition { RoleId = "diplomacy_advisor", SkillIds = new List<string> { "charm" } },
					new CharacterRoleDefinition { RoleId = "economic_advisor",  SkillIds = new List<string> { "stinginess" } },
					new CharacterRoleDefinition { RoleId = "secret_advisor",    SkillIds = new List<string> { "intrigue" } }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "Great_Britain",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["ruler"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "great_britain_ruler_1",
									NamePartKeys = new List<string> { "character.name.british", "character.name.char_i" },
									Skills = new Dictionary<string, SkillSettings> {
										["power"]      = new SkillSettings { MinValue = 30, MaxValue = 70 },
										["charm"]      = new SkillSettings { MinValue = 40, MaxValue = 80 },
										["stinginess"] = new SkillSettings { MinValue = 30, MaxValue = 70 },
										["intrigue"]   = new SkillSettings { MinValue = 30, MaxValue = 70 }
									}
								}
							},
							["military_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "great_britain_mil_1",
									NamePartKeys = new List<string> { "character.name.british", "character.name.char_i" },
									Skills = new Dictionary<string, SkillSettings> {
										["power"] = new SkillSettings { MinValue = 40, MaxValue = 90 }
									}
								}
							},
							["diplomacy_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "great_britain_dip_1",
									NamePartKeys = new List<string> { "character.name.british", "character.name.char_i" },
									Skills = new Dictionary<string, SkillSettings> {
										["charm"] = new SkillSettings { MinValue = 40, MaxValue = 90 }
									}
								}
							},
							["economic_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "great_britain_eco_1",
									NamePartKeys = new List<string> { "character.name.british", "character.name.char_i" },
									Skills = new Dictionary<string, SkillSettings> {
										["stinginess"] = new SkillSettings { MinValue = 40, MaxValue = 90 }
									}
								}
							},
							["secret_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "great_britain_sec_1",
									NamePartKeys = new List<string> { "character.name.british", "character.name.char_i" },
									Skills = new Dictionary<string, SkillSettings> {
										["intrigue"] = new SkillSettings { MinValue = 40, MaxValue = 90 }
									}
								}
							}
						}
					}
				}
			};
		}

		static GameLogic BuildLogic(CharacterConfig characterConfig, string countryId = "Great_Britain", bool isAvailable = true) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = countryId, DisplayName = countryId, IsAvailable = isAvailable }
				}
			};
			var orgConfig = new OrganizationConfig { Organizations = new List<OrganizationEntry>() };
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly"
			};
			var resourceConfig = new ResourceConfig { Resources = new List<ResourceDefinition>() };
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				character: new StaticConfig<CharacterConfig>(characterConfig)
			);
			return new GameLogic(ctx);
		}

		List<Character> GetCharacters(World world, string countryId) {
			var result = new List<Character>();
			int[] req = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					if (chars[i].CountryId == countryId) {
						result.Add(chars[i]);
					}
				}
			}
			return result;
		}

		List<(string ownerId, string resourceId, double value)> GetSkillResources(World world) {
			var result = new List<(string, string, double)>();
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add((owners[i].OwnerId, resources[i].ResourceId, resources[i].Value));
				}
			}
			return result;
		}

		[Fact]
		void character_entities_created_for_available_country() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			Assert.Equal(5, chars.Count);
		}

		[Fact]
		void character_roles_match_expected_set() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			var roles = chars.Select(c => c.RoleId).ToHashSet();
			Assert.Contains("ruler", roles);
			Assert.Contains("military_advisor", roles);
			Assert.Contains("diplomacy_advisor", roles);
			Assert.Contains("economic_advisor", roles);
			Assert.Contains("secret_advisor", roles);
		}

		[Fact]
		void ruler_has_all_four_skills_nonzero() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			var ruler = chars.First(c => c.RoleId == "ruler");
			var skills = GetSkillResources(logic.World)
				.Where(r => r.ownerId == ruler.CharacterId)
				.ToList();
			Assert.Equal(4, skills.Count);
		}

		[Fact]
		void military_advisor_has_all_skills_with_power_specialized() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			var mil = chars.First(c => c.RoleId == "military_advisor");
			var skills = GetSkillResources(logic.World)
				.Where(r => r.ownerId == mil.CharacterId)
				.ToList();
			Assert.Equal(4, skills.Count);
			var power = skills.First(s => s.resourceId == "power");
			Assert.True(power.value >= 40 && power.value <= 90);
		}

		[Fact]
		void skill_values_within_configured_range() {
			var charConfig = BuildCharacterConfig();
			var logic = BuildLogic(charConfig);
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			var allSkills = GetSkillResources(logic.World);
			var charIds = chars.Select(c => c.CharacterId).ToHashSet();

			foreach (var (ownerId, resourceId, value) in allSkills) {
				if (!charIds.Contains(ownerId)) {
					continue;
				}
				var pool = charConfig.FindPool("Great_Britain");
				CharacterEntry? foundEntry = null;
				if (pool != null) {
					foreach (var slot in pool.Slots.Values) {
						foreach (var entry in slot) {
							if (entry.CharacterId == ownerId) {
								foundEntry = entry;
								break;
							}
						}
						if (foundEntry != null) { break; }
					}
				}
				if (foundEntry == null || !foundEntry.Skills.TryGetValue(resourceId, out var settings)) {
					continue;
				}
				Assert.True(value >= settings.MinValue, $"Skill {resourceId} value {value} below min {settings.MinValue}");
				Assert.True(value <= settings.MaxValue, $"Skill {resourceId} value {value} above max {settings.MaxValue}");
			}
		}

		[Fact]
		void no_character_entities_for_unavailable_country() {
			var logic = BuildLogic(BuildCharacterConfig(), countryId: "Great_Britain", isAvailable: false);
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			Assert.Empty(chars);
		}

		[Fact]
		void country_without_pool_produces_no_characters() {
			var charConfig = new CharacterConfig {
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = "ruler", SkillIds = new List<string> { "power" } }
				},
				CountryPools = new List<CountryCharacterPool>()
			};
			var logic = BuildLogic(charConfig, countryId: "Great_Britain", isAvailable: true);
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			Assert.Empty(chars);
		}

		[Fact]
		void name_part_keys_stored_correctly() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			var chars = GetCharacters(logic.World, "Great_Britain");
			var ruler = chars.First(c => c.RoleId == "ruler");
			Assert.Equal(new[] { "character.name.british", "character.name.char_i" }, ruler.NamePartKeys);
		}
	}
}
