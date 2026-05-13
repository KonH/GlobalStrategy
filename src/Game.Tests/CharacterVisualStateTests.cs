using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CharacterVisualStateTests {
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

		static GameLogic BuildLogic(CharacterConfig characterConfig) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true }
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

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				character: new StaticConfig<CharacterConfig>(characterConfig)
			);
			return new GameLogic(ctx);
		}

		void SelectCountry(World world, string countryId) {
			int[] req = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Country[] countries = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) {
					if (countries[i].CountryId == countryId) {
						world.Add(arch.Entities[i], new IsSelected());
						return;
					}
				}
			}
		}

		[Fact]
		void characters_state_empty_when_no_country_selected() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			Assert.Empty(logic.VisualState.SelectedCharacters.Characters);
		}

		[Fact]
		void characters_state_populated_when_country_selected() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			SelectCountry(logic.World, "Great_Britain");
			logic.Update(0f);
			Assert.Equal(5, logic.VisualState.SelectedCharacters.Characters.Count);
		}

		[Fact]
		void character_state_entries_have_correct_role_ids() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			SelectCountry(logic.World, "Great_Britain");
			logic.Update(0f);
			var roleIds = new HashSet<string>();
			foreach (var c in logic.VisualState.SelectedCharacters.Characters) {
				roleIds.Add(c.RoleId);
			}
			Assert.Contains("ruler", roleIds);
			Assert.Contains("military_advisor", roleIds);
			Assert.Contains("diplomacy_advisor", roleIds);
			Assert.Contains("economic_advisor", roleIds);
			Assert.Contains("secret_advisor", roleIds);
		}

		[Fact]
		void character_state_entries_have_skills_populated() {
			var logic = BuildLogic(BuildCharacterConfig());
			logic.Update(0f);
			SelectCountry(logic.World, "Great_Britain");
			logic.Update(0f);
			foreach (var c in logic.VisualState.SelectedCharacters.Characters) {
				Assert.NotEmpty(c.Skills);
				foreach (var skill in c.Skills) {
					Assert.True(skill.Value > 0);
				}
			}
		}
	}
}
