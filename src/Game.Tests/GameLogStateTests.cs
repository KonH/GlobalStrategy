using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	// Covers Docs/Specs/26_07_18_07_action-log-ui/plan.md — the Action Log feature's
	// UpdateGameLog collection logic. Follows the DiscoverAndControlFeatureTests /
	// CharacterVisualStateTests convention: bespoke GameLogicContext/ActionConfig/EffectConfig
	// per scenario, GameLogic driven directly via Update(...) and Commands.Push(...).
	public class GameLogStateTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string OrgBId = "Masons";
		const string HqCountryId = "Great_Britain";
		const string OtherCountryId = "France";
		const string CountryA = "Austria";
		const string CountryB = "Prussia";
		const string CountryC = "Spain";

		static GameLogicContext BuildContext(
			ActionConfig actionConfig,
			EffectConfig effectConfig,
			CharacterConfig? characterConfig = null,
			GameSettings? gameSettings = null,
			OrganizationConfig? orgConfig = null,
			CountryConfig? countryConfig = null,
			int? rngSeed = null,
			IReadOnlyList<string>? participatingOrgIds = null) {

			countryConfig ??= new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
				}
			};
			orgConfig ??= new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = OrgId, DisplayName = "Illuminati", HqCountryId = HqCountryId,
						InitialGold = 1000.0
					}
				}
			};
			gameSettings ??= new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 24, 720 },
				AutoSaveInterval = "monthly"
			};
			var resourceConfig = new ResourceConfig { Resources = new List<ResourceDefinition>() };

			return new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: OrgId,
				character: characterConfig != null ? new StaticConfig<CharacterConfig>(characterConfig) : null,
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(effectConfig),
				rngSeed: rngSeed,
				participatingOrganizationIds: participatingOrgIds);
		}

		static GameLogic BuildLogic(
			ActionConfig actionConfig, EffectConfig effectConfig,
			CharacterConfig? characterConfig = null, GameSettings? gameSettings = null,
			OrganizationConfig? orgConfig = null, CountryConfig? countryConfig = null, int? rngSeed = null,
			IReadOnlyList<string>? participatingOrgIds = null) {
			return new GameLogic(BuildContext(actionConfig, effectConfig, characterConfig, gameSettings, orgConfig, countryConfig, rngSeed, participatingOrgIds));
		}

		static IReadOnlyList<GameLogEntry> Entries(GameLogic logic) => logic.VisualState.GameLog.Entries;

		[Fact]
		void discovery_produces_exactly_one_entry_and_no_extra_on_a_passive_tick() {
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = OrgId, ActionIds = new List<string> { "spread_rumors" } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "spread_rumors", OwnerType = "org", EffectIds = new List<string> { "discover" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);

			var discoveries = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Discovery).ToList();
			Assert.Single(discoveries);
			Assert.Equal(OrgId, discoveries[0].OrgId);
			Assert.Equal(OtherCountryId, discoveries[0].CountryId);

			// Passive tick, no new PlayCardActionCommand — no additional entry.
			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Discovery));

			// DiscoveryApplied and DiscoveredCountry are independent sibling entities — confirm
			// the persistent record still exists alongside the (already-swept) transient log event.
			bool discoveredCountryExists = false;
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == OrgId && dcs[i].CountryId == OtherCountryId) { discoveredCountryExists = true; }
				}
			}
			Assert.True(discoveredCountryExists);
		}

		static ActionConfig ControlActionConfig(int deckCopies) => new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "country", HandSize = deckCopies }
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition { ActionId = "raise_control", OwnerType = "country", DeckCopies = deckCopies, EffectIds = new List<string> { "control_gain" } }
			}
		};

		static EffectConfig ControlEffectConfig(int amount) => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new ControlChangeEffectParams { EffectId = "control_gain", EffectType = "ControlChange", Amount = amount }
			}
		};

		[Fact]
		void control_entries_carry_independent_delta_and_running_total() {
			var logic = BuildLogic(ControlActionConfig(2), ControlEffectConfig(5));
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);
			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Single(controls);
			Assert.Equal(5, controls[0].Delta);
			Assert.Equal(5, controls[0].Total);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);
			controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Equal(2, controls.Count);
			Assert.Equal(5, controls[1].Delta);
			Assert.Equal(10, controls[1].Total);
		}

		// Regression test for the bug caught in plan review: ControlEffectApplied.Total must be
		// the ACTING ORG's own control total in the country (GetOrgControlInCountry), not
		// GetTotalControlInCountry's all-orgs shared-pool total used only for the 100-point cap
		// check. See Docs/Specs/26_07_18_07_action-log-ui/plan.md.
		[Fact]
		void control_total_is_per_org_not_shared_pool_total() {
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = OrgId, DisplayName = "Illuminati", HqCountryId = HqCountryId, InitialGold = 1000.0 },
					new OrganizationEntry { OrganizationId = OrgBId, DisplayName = "Masons", HqCountryId = "Prussia", InitialGold = 1000.0 }
				}
			};
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", DisplayName = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
				}
			};
			var logic = BuildLogic(ControlActionConfig(1), ControlEffectConfig(5), orgConfig: orgConfig, countryConfig: countryConfig,
				participatingOrgIds: new List<string> { OrgId, OrgBId });
			logic.Update(0f);

			// Second org raises control in the shared target country first.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgBId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);

			// First org raises control in the same country next.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);

			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Equal(2, controls.Count);
			var orgAEntry = controls.Find(c => c.OrgId == OrgId);
			Assert.NotNull(orgAEntry);
			// Must equal only OrgId's own contribution (5), not the combined pool (10).
			Assert.Equal(5, orgAEntry!.Total);
		}

		static CharacterConfig OpinionCharacterConfig(string charId, string countryId) => new CharacterConfig {
			Roles = new List<CharacterRoleDefinition> { new CharacterRoleDefinition { RoleId = "ruler" } },
			CountryPools = new List<CountryCharacterPool> {
				new CountryCharacterPool {
					CountryId = countryId,
					Slots = new Dictionary<string, List<CharacterEntry>> {
						["ruler"] = new List<CharacterEntry> { new CharacterEntry { CharacterId = charId } }
					}
				}
			}
		};

		[Fact]
		void opinion_delta_total_and_decay_only_tick_produces_zero_entries() {
			const string charId = "napoleon";
			var characterConfig = OpinionCharacterConfig(charId, OtherCountryId);
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 2 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "improve_opinion", OwnerType = "country", TargetRole = "ruler", DeckCopies = 2, EffectIds = new List<string> { "opinion_boost" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new OpinionModifierEffectParams { EffectId = "opinion_boost", EffectType = "OpinionModifier", InitialValue = 20, DecayPerMonth = 5 }
				}
			};
			var logic = BuildLogic(actionConfig, effectConfig, characterConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "improve_opinion" });
			logic.Update(0f);
			var opinions = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Opinion).ToList();
			Assert.Single(opinions);
			Assert.Equal(20, opinions[0].Delta);
			Assert.Equal(20, opinions[0].Total);

			int countBeforeDecay = Entries(logic).Count;
			// Advance 31 days (default x1 multiplier), crossing the month boundary so the
			// monthly decay effect fires. Decay does not go through CreateActionEffectSystem,
			// so no OpinionEffectApplied is created for it.
			for (int day = 0; day < 31; day++) { logic.Update(24f); }
			Assert.Equal(countBeforeDecay, Entries(logic).Count);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "improve_opinion" });
			logic.Update(0f);
			opinions = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Opinion).ToList();
			Assert.Equal(2, opinions.Count);
			Assert.Equal(20, opinions[1].Delta);
			// Total reflects the decayed-then-raised value (15 + 20 = 35), not a naive sum of raises.
			Assert.Equal(35, opinions[1].Total);
		}

		static CharacterConfig NewCharacterConfig() => new CharacterConfig {
			Skills = new List<CharacterSkillDefinition>(),
			Roles = new List<CharacterRoleDefinition> {
				new CharacterRoleDefinition { RoleId = "ruler", SkillIds = new List<string>() },
				new CharacterRoleDefinition { RoleId = "master", SkillIds = new List<string>(), MaxCount = 1 }
			},
			CountryPools = new List<CountryCharacterPool> {
				new CountryCharacterPool {
					CountryId = HqCountryId,
					Slots = new Dictionary<string, List<CharacterEntry>> {
						["ruler"] = new List<CharacterEntry> {
							new CharacterEntry { CharacterId = "gb_ruler_1", NamePartKeys = new List<string> { "character.name.british" } },
							new CharacterEntry { CharacterId = "gb_ruler_2", NamePartKeys = new List<string> { "character.name.british2" } }
						}
					}
				}
			},
			OrgPools = new List<OrgCharacterPool> {
				new OrgCharacterPool {
					OrgId = OrgId,
					Slots = new Dictionary<string, List<CharacterEntry>> {
						["master"] = new List<CharacterEntry> {
							new CharacterEntry { CharacterId = "illuminati_master_1", NamePartKeys = new List<string> { "character.name.part.adam" } },
							new CharacterEntry { CharacterId = "illuminati_master_2", NamePartKeys = new List<string> { "character.name.part.weishaupt" } }
						}
					}
				}
			}
		};

		static GameLogic BuildCharacterLogic() {
			var actionConfig = new ActionConfig();
			var effectConfig = new EffectConfig();
			return BuildLogic(actionConfig, effectConfig, NewCharacterConfig());
		}

		// This is also the regression test for the RoleChangeApplied same-tick-destruction bug
		// caught in plan review: with the original (buggy) plan,
		// CleanupEffectNotificationsSystem.UpdateRoleChange sharing UpdateActionEffects' call
		// site would have destroyed RoleChangeApplied before VisualStateConverter.Update ever
		// ran, and this test would fail with zero NewCharacter entries. See
		// Docs/Specs/26_07_18_07_action-log-ui/plan.md "Cleanup wiring" section.
		[Fact]
		void new_character_org_role_and_country_role_each_produce_exactly_one_entry() {
			var logic = BuildCharacterLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugCycleCharacterCommand { OwnerId = OrgId, RoleId = "master", SlotIndex = 0 });
			logic.Update(0f);
			var entries = Entries(logic).Where(e => e.Kind == GameLogEntryKind.NewCharacter).ToList();
			Assert.Single(entries);
			Assert.True(entries[0].IsOrgRole);
			Assert.Equal(OrgId, entries[0].OrgId);
			Assert.Equal("", entries[0].CountryId);

			logic.Commands.Push(new DebugCycleCharacterCommand { OwnerId = HqCountryId, RoleId = "ruler", SlotIndex = 0 });
			logic.Update(0f);
			entries = Entries(logic).Where(e => e.Kind == GameLogEntryKind.NewCharacter).ToList();
			Assert.Equal(2, entries.Count);
			var countryEntry = entries.Find(e => !e.IsOrgRole);
			Assert.NotNull(countryEntry);
			Assert.Equal(HqCountryId, countryEntry!.CountryId);
			Assert.Equal("", countryEntry.OrgId);

			// Dropping a character never counts as "new" — no additional entry.
			logic.Commands.Push(new DebugDropCharacterCommand { OwnerId = OrgId, RoleId = "master", SlotIndex = 0 });
			logic.Update(0f);
			entries = Entries(logic).Where(e => e.Kind == GameLogEntryKind.NewCharacter).ToList();
			Assert.Equal(2, entries.Count);
		}

		[Fact]
		void no_entries_after_initial_seeding_update() {
			var logic = BuildCharacterLogic();
			logic.Update(0f);
			Assert.Empty(Entries(logic));
		}

		[Fact]
		void include_player_actions_false_suppresses_only_player_org_entries() {
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = OrgId, DisplayName = "Illuminati", HqCountryId = HqCountryId, InitialGold = 1000.0 },
					new OrganizationEntry { OrganizationId = OrgBId, DisplayName = "Masons", HqCountryId = "Prussia", InitialGold = 1000.0 }
				}
			};
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", DisplayName = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> { new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 } },
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = OrgId, ActionIds = new List<string> { "spread_rumors" } },
					new OrgActionPool { OrgId = OrgBId, ActionIds = new List<string> { "spread_rumors" } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "spread_rumors", OwnerType = "org", EffectIds = new List<string> { "discover" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly",
				GameLog = new GameLogSettings { IncludePlayerActions = false, MaxLogEntries = 12 }
			};
			var characterConfig = NewCharacterConfig();
			var logic = BuildLogic(actionConfig, effectConfig, characterConfig, gameSettings, orgConfig, countryConfig,
				participatingOrgIds: new List<string> { OrgId, OrgBId });
			logic.Update(0f);

			// Player org (Illuminati, the initialOrganizationId) discovery — suppressed.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);
			Assert.Empty(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Discovery));

			// AI org discovery — still appears.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgBId, ActionId = "spread_rumors" });
			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Discovery && e.OrgId == OrgBId));

			// Country-role NewCharacter (no acting org) — never suppressed.
			logic.Commands.Push(new DebugCycleCharacterCommand { OwnerId = HqCountryId, RoleId = "ruler", SlotIndex = 0 });
			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.NewCharacter && !e.IsOrgRole));
		}

		[Fact]
		void max_log_entries_caps_and_evicts_oldest_first() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = CountryA, DisplayName = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = CountryB, DisplayName = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = CountryC, DisplayName = "Spain", IsAvailable = true }
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> { new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 } },
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "raise_control", OwnerType = "country", DeckCopies = 1, EffectIds = new List<string> { "control_gain" } }
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly",
				GameLog = new GameLogSettings { IncludePlayerActions = true, MaxLogEntries = 2 }
			};
			var logic = BuildLogic(actionConfig, ControlEffectConfig(5), gameSettings: gameSettings, countryConfig: countryConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryA, ActionId = "raise_control" });
			logic.Update(0f);
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryB, ActionId = "raise_control" });
			logic.Update(0f);
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryC, ActionId = "raise_control" });
			logic.Update(0f);

			var entries = Entries(logic);
			Assert.Equal(2, entries.Count);
			Assert.Equal(CountryB, entries[0].CountryId);
			Assert.Equal(CountryC, entries[1].CountryId);
		}
	}
}
