using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class OrgCharacterTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static CharacterConfig BuildCharacterConfig(int masterCandidates = 1, int agentCandidates = 3) {
			var masterEntries = new List<CharacterEntry>();
			for (int i = 0; i < masterCandidates; i++) {
				int idx = i + 1;
				masterEntries.Add(new CharacterEntry {
					CharacterId = $"illuminati_master_{idx}",
					NamePartKeys = new List<string> { "character.name.part.adam", "character.name.part.weishaupt" },
					Skills = new Dictionary<string, SkillSettings> {
						["charm"] = new SkillSettings { MinValue = 70, MaxValue = 95 }
					}
				});
			}
			var agentEntries = new List<CharacterEntry>();
			for (int i = 0; i < agentCandidates; i++) {
				int idx = i + 1;
				agentEntries.Add(new CharacterEntry {
					CharacterId = $"illuminati_agent_{idx}",
					NamePartKeys = new List<string> { "character.name.part.xavier", "character.name.part.delvaux" },
					Skills = new Dictionary<string, SkillSettings> {
						["intrigue"] = new SkillSettings { MinValue = 60, MaxValue = 90 }
					}
				});
			}
			return new CharacterConfig {
				Skills = new List<CharacterSkillDefinition> {
					new CharacterSkillDefinition { SkillId = "charm" },
					new CharacterSkillDefinition { SkillId = "intrigue" }
				},
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = "ruler", SkillIds = new List<string>() },
					new CharacterRoleDefinition { RoleId = "master", SkillIds = new List<string> { "charm" }, MaxCount = 1 },
					new CharacterRoleDefinition { RoleId = "agent",  SkillIds = new List<string> { "intrigue" }, MaxCount = 3 }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "Great_Britain",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["ruler"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "gb_ruler_1",
									NamePartKeys = new List<string> { "character.name.british" },
									Skills = new Dictionary<string, SkillSettings>()
								},
								new CharacterEntry {
									CharacterId = "gb_ruler_2",
									NamePartKeys = new List<string> { "character.name.british2" },
									Skills = new Dictionary<string, SkillSettings>()
								}
							}
						}
					}
				},
				OrgPools = new List<OrgCharacterPool> {
					new OrgCharacterPool {
						OrgId = "Illuminati",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["master"] = masterEntries,
							["agent"]  = agentEntries
						}
					}
				}
			};
		}

		static GameLogic BuildOrgCharacterLogic(CharacterConfig? charConfig = null, int agentSlots = 3) {
			charConfig ??= BuildCharacterConfig();
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000,
						InitialAgentSlots = agentSlots
					}
				}
			};
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
				character: new StaticConfig<CharacterConfig>(charConfig),
				initialOrganizationId: "Illuminati"
			);
			return new GameLogic(ctx);
		}

		List<CharacterSlot> GetCharacterSlots(World world, string ownerId) {
			var result = new List<CharacterSlot>();
			int[] req = { TypeId<CharacterSlot>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
				for (int i = 0; i < arch.Count; i++) {
					if (slots[i].OwnerId == ownerId) {
						result.Add(slots[i]);
					}
				}
			}
			return result;
		}

		List<Character> GetOrgCharacters(World world, string orgId) {
			var result = new List<Character>();
			int[] req = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					if (chars[i].OrgId == orgId) {
						result.Add(chars[i]);
					}
				}
			}
			return result;
		}

		[Fact]
		void org_master_slot_entity_created() {
			var logic = BuildOrgCharacterLogic();
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlots = slots.FindAll(s => s.RoleId == "master");
			Assert.Equal(1, masterSlots.Count);
		}

		[Fact]
		void org_agent_slot_entities_count_matches_config() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var agentSlots = slots.FindAll(s => s.RoleId == "agent");
			Assert.Equal(3, agentSlots.Count);
		}

		[Fact]
		void org_master_slot_index_zero_has_character() {
			var logic = BuildOrgCharacterLogic();
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlot = slots.Find(s => s.RoleId == "master" && s.SlotIndex == 0);
			Assert.NotNull(masterSlot.CharacterId);
			Assert.NotEmpty(masterSlot.CharacterId);
		}

		[Fact]
		void org_agent_slot_index_zero_has_character() {
			var logic = BuildOrgCharacterLogic();
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var agentSlot0 = slots.Find(s => s.RoleId == "agent" && s.SlotIndex == 0);
			Assert.NotEmpty(agentSlot0.CharacterId);
		}

		[Fact]
		void org_agent_slots_1_and_2_are_empty() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var slot1 = slots.Find(s => s.RoleId == "agent" && s.SlotIndex == 1);
			var slot2 = slots.Find(s => s.RoleId == "agent" && s.SlotIndex == 2);
			Assert.Empty(slot1.CharacterId);
			Assert.Empty(slot2.CharacterId);
		}

		[Fact]
		void player_org_slots_are_available() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var slot1 = slots.Find(s => s.RoleId == "agent" && s.SlotIndex == 1);
			var slot2 = slots.Find(s => s.RoleId == "agent" && s.SlotIndex == 2);
			Assert.True(slot1.IsAvailable);
			Assert.True(slot2.IsAvailable);
		}

		[Fact]
		void update_org_characters_populates_player_org_characters() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			// 1 master slot + 3 agent slots = 4
			Assert.Equal(4, logic.VisualState.PlayerOrgCharacters.Slots.Count);
		}

		[Fact]
		void filled_slot_has_character_entry() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			var slots = logic.VisualState.PlayerOrgCharacters.Slots;
			OrgCharacterSlotEntry? masterSlot = null;
			foreach (var s in slots) {
				if (s.RoleId == "master" && s.SlotIndex == 0) { masterSlot = s; break; }
			}
			Assert.NotNull(masterSlot);
			Assert.NotNull(masterSlot.Character);
		}

		[Fact]
		void empty_slot_has_null_character_entry() {
			var logic = BuildOrgCharacterLogic(agentSlots: 3);
			logic.Update(0f);
			var slots = logic.VisualState.PlayerOrgCharacters.Slots;
			OrgCharacterSlotEntry? agentSlot1 = null;
			foreach (var s in slots) {
				if (s.RoleId == "agent" && s.SlotIndex == 1) { agentSlot1 = s; break; }
			}
			Assert.NotNull(agentSlot1);
			Assert.Null(agentSlot1.Character);
		}

		[Fact]
		void debug_cycle_with_two_candidates_switches_character() {
			var charConfig = BuildCharacterConfig(masterCandidates: 2);
			var logic = BuildOrgCharacterLogic(charConfig);
			logic.Update(0f);
			// Find initial master character
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlot = slots.Find(s => s.RoleId == "master" && s.SlotIndex == 0);
			string initial = masterSlot.CharacterId;
			// Cycle
			logic.Commands.Push(new GS.Game.Commands.DebugCycleCharacterCommand { OwnerId = "Illuminati", RoleId = "master", SlotIndex = 0 });
			logic.Update(0f);
			var slotsAfter = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlotAfter = slotsAfter.Find(s => s.RoleId == "master" && s.SlotIndex == 0);
			Assert.NotEqual(initial, masterSlotAfter.CharacterId);
		}

		[Fact]
		void debug_drop_org_character_empties_slot() {
			var logic = BuildOrgCharacterLogic();
			logic.Update(0f);
			logic.Commands.Push(new GS.Game.Commands.DebugDropCharacterCommand { OwnerId = "Illuminati", RoleId = "master", SlotIndex = 0 });
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlot = slots.Find(s => s.RoleId == "master" && s.SlotIndex == 0);
			Assert.Empty(masterSlot.CharacterId);
		}

		[Fact]
		void debug_drop_player_org_sets_available() {
			var logic = BuildOrgCharacterLogic();
			logic.Update(0f);
			logic.Commands.Push(new GS.Game.Commands.DebugDropCharacterCommand { OwnerId = "Illuminati", RoleId = "master", SlotIndex = 0 });
			logic.Update(0f);
			var slots = GetCharacterSlots(logic.World, "Illuminati");
			var masterSlot = slots.Find(s => s.RoleId == "master" && s.SlotIndex == 0);
			Assert.True(masterSlot.IsAvailable);
		}

		[Fact]
		void debug_cycle_country_character_switches() {
			var charConfig = BuildCharacterConfig();
			var logic = BuildOrgCharacterLogic(charConfig);
			logic.Update(0f);
			// Get current ruler
			string initial = FindCountryCharacterId(logic.World, "Great_Britain", "ruler");
			Assert.NotEmpty(initial);
			// Cycle
			logic.Commands.Push(new GS.Game.Commands.DebugCycleCharacterCommand { OwnerId = "Great_Britain", RoleId = "ruler", SlotIndex = 0 });
			logic.Update(0f);
			string after = FindCountryCharacterId(logic.World, "Great_Britain", "ruler");
			Assert.NotEqual(initial, after);
		}

		[Fact]
		void debug_drop_country_character_removes_entity() {
			var charConfig = BuildCharacterConfig();
			var logic = BuildOrgCharacterLogic(charConfig);
			logic.Update(0f);
			logic.Commands.Push(new GS.Game.Commands.DebugDropCharacterCommand { OwnerId = "Great_Britain", RoleId = "ruler", SlotIndex = 0 });
			logic.Update(0f);
			string after = FindCountryCharacterId(logic.World, "Great_Britain", "ruler");
			Assert.Empty(after);
		}

		string FindCountryCharacterId(World world, string countryId, string roleId) {
			int[] req = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					if (chars[i].CountryId == countryId && chars[i].RoleId == roleId) {
						return chars[i].CharacterId;
					}
				}
			}
			return "";
		}
	}
}
