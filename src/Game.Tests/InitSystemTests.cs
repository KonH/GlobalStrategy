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
	public class InitSystemTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		sealed class MemoryStorage : IPersistentStorage {
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

		sealed class CapturingSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _store = new Dictionary<string, WorldSnapshot>();
			public string LastSaveName { get; private set; } = "";

			public string Serialize(WorldSnapshot snapshot) {
				LastSaveName = snapshot.Header.SaveName;
				_store[snapshot.Header.SaveName] = snapshot;
				return snapshot.Header.SaveName;
			}

			public WorldSnapshot Deserialize(string json) => _store[json];
		}

		static GameLogic BuildLogic(
			IPersistentStorage? storage = null, ISnapshotSerializer? serializer = null, GameSettings? gameSettingsOverride = null,
			ActionConfig? actionConfigOverride = null, CharacterConfig? characterConfigOverride = null,
			OrganizationConfig? organizationConfigOverride = null, IReadOnlyList<string>? participatingOrganizationIds = null) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true }
				}
			};
			var orgConfig = organizationConfigOverride ?? new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var gameSettings = gameSettingsOverride ?? new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly"
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryPopulation },
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryScore },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Recruits },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Population, SeedTarget = ResourceSeedTarget.Province },
					new ResourceDefinition { ResourceId = ResourceDefinitions.OrgScore, SeedTarget = ResourceSeedTarget.Org },
					new ResourceDefinition { ResourceId = ResourceDefinitions.WarInitiative, SeedTarget = ResourceSeedTarget.Country }
				}
			};
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain", Population = 1234.0 },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = "France", Population = 5678.0 }
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				storage: storage,
				serializer: serializer,
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(provinceConfig),
				action: actionConfigOverride != null ? new StaticConfig<ActionConfig>(actionConfigOverride) : null,
				character: characterConfigOverride != null ? new StaticConfig<CharacterConfig>(characterConfigOverride) : null,
				participatingOrganizationIds: participatingOrganizationIds);
			return new GameLogic(ctx);
		}

		static int CountEntities<T>(World world) {
			int count = 0;
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				count += arch.Count;
			}
			return count;
		}

		static double? GetResourceValue(World world, string ownerId, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return resources[i].Value;
					}
				}
			}
			return null;
		}

		static double? GetEffectValue(World world, string ownerId, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<ResourceLink>.Value, TypeId<ResourceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId && links[i].ResourceId == resourceId) {
						return effects[i].Value;
					}
				}
			}
			return null;
		}

		[Fact]
		void world_is_empty_before_first_update() {
			var logic = BuildLogic();
			Assert.Equal(0, CountEntities<Country>(logic.World));
		}

		[Fact]
		void world_is_populated_after_first_update() {
			var logic = BuildLogic();
			logic.Update(0f);
			Assert.True(CountEntities<Country>(logic.World) > 0);
		}

		[Fact]
		void init_does_not_run_twice() {
			var logic = BuildLogic();
			logic.Update(0f);
			int countFirst = CountEntities<Country>(logic.World);
			logic.Update(0f);
			Assert.Equal(countFirst, CountEntities<Country>(logic.World));
		}

		[Fact]
		void init_creates_one_in_progress_completion_singleton() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(1, CountEntities<GameCompletion>(logic.World));
			int[] required = { TypeId<GameCompletion>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				GameCompletion[] completions = arch.GetColumn<GameCompletion>();
				Assert.False(completions[0].IsCompleted);
				Assert.Equal("", completions[0].WinnerOrganizationId);
			}
		}

		[Fact]
		void init_attaches_ordered_in_progress_outcome_to_participant() {
			var logic = BuildLogic();
			logic.Update(0f);

			int[] required = { TypeId<Organization>.Value, TypeId<OrganizationGameOutcome>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = arch.GetColumn<Organization>();
				OrganizationGameOutcome[] outcomes = arch.GetColumn<OrganizationGameOutcome>();
				Assert.Equal("Illuminati", organizations[0].OrganizationId);
				Assert.Equal(0, outcomes[0].ParticipationOrder);
				Assert.Equal(OrganizationGameResult.InProgress, outcomes[0].Result);
			}
		}

		[Fact]
		void init_creates_country_relations_version_and_relation_card_sync_state_singletons() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(1, CountEntities<CountryRelationsVersion>(logic.World));
			int[] versionReq = { TypeId<CountryRelationsVersion>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(versionReq, null)) {
				CountryRelationsVersion[] versions = arch.GetColumn<CountryRelationsVersion>();
				for (int i = 0; i < arch.Count; i++) {
					Assert.Equal(0, versions[i].Value);
				}
			}

			// LastSyncedVersion starts at -1 in InitSystem's raw seed, but RelationCardSyncSystem
			// runs later in this same GameLogic.Update tick (before DrawCardSystem, per
			// Docs/Specs/26_07_24_13_stop-friendship-rivalry-cards/plan.md Step 12) and immediately
			// catches it up to the current CountryRelationsVersion.Value (0, since no relation
			// exists yet) so newly-synced cards are eligible for this same tick's draw.
			Assert.Equal(1, CountEntities<RelationCardSyncState>(logic.World));
			int[] syncReq = { TypeId<RelationCardSyncState>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(syncReq, null)) {
				RelationCardSyncState[] states = arch.GetColumn<RelationCardSyncState>();
				for (int i = 0; i < arch.Count; i++) {
					Assert.Equal(0, states[i].LastSyncedVersion);
				}
			}
		}

		[Fact]
		void init_skipped_after_load() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);

			logic.Update(0f);
			int countAfterInit = CountEntities<Country>(logic.World);

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);

			string saveName = serializer.LastSaveName;
			logic.LoadState(saveName);
			logic.Update(0f);

			Assert.Equal(countAfterInit, CountEntities<Country>(logic.World));
		}

		[Fact]
		void province_ownership_seeded_once_from_config() {
			var logic = BuildLogic();
			logic.Update(0f);
			int countAfterInit = CountEntities<ProvinceOwnership>(logic.World);
			Assert.Equal(2, countAfterInit);

			logic.Update(0f);
			Assert.Equal(countAfterInit, CountEntities<ProvinceOwnership>(logic.World));
		}

		[Fact]
		void province_population_seeded_from_config() {
			var logic = BuildLogic();
			logic.Update(0f);

			var populationByProvinceId = new Dictionary<string, (double Value, OwnerType OwnerType)>();
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (resources[i].ResourceId == "population") {
						populationByProvinceId[owners[i].OwnerId] = (resources[i].Value, owners[i].OwnerType);
					}
				}
			}

			Assert.Equal(2, populationByProvinceId.Count);
			Assert.Equal((1234.0, OwnerType.Province), populationByProvinceId["prov_a"]);
			Assert.Equal((5678.0, OwnerType.Province), populationByProvinceId["prov_b"]);
		}

		[Fact]
		void country_population_and_score_seeded_at_init_from_province_population() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(1234.0, GetResourceValue(logic.World, "Great_Britain", "country_population"));
			Assert.Equal(5678.0, GetResourceValue(logic.World, "France", "country_population"));
			Assert.Equal(1234.0, GetResourceValue(logic.World, "Great_Britain", "country_score"));
			Assert.Equal(5678.0, GetResourceValue(logic.World, "France", "country_score"));
		}

		[Fact]
		void org_score_seeded_at_init_from_base_control_times_country_score() {
			var logic = BuildLogic();
			logic.Update(0f);

			// Illuminati's default base control (10) in its HQ (Great_Britain, country_score 1234.0).
			Assert.Equal(123.4, GetResourceValue(logic.World, "Illuminati", "org_score")!.Value, 6);
		}

		[Fact]
		void country_score_correct_immediately_after_load_with_no_forced_recompute() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);

			logic.Update(0f);
			double scoreGBBeforeSave = GetResourceValue(logic.World, "Great_Britain", "country_score")!.Value;
			double scoreFRBeforeSave = GetResourceValue(logic.World, "France", "country_score")!.Value;
			Assert.Equal(1234.0, scoreGBBeforeSave);
			Assert.Equal(5678.0, scoreFRBeforeSave);

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loadedLogic = BuildLogic(storage, serializer);
			loadedLogic.LoadState(saveName);

			Assert.Equal(scoreGBBeforeSave, GetResourceValue(loadedLogic.World, "Great_Britain", "country_score"));
			Assert.Equal(scoreFRBeforeSave, GetResourceValue(loadedLogic.World, "France", "country_score"));
		}

		[Fact]
		void province_population_unaffected_on_first_tick() {
			var logic = BuildLogic();
			logic.Update(0f);

			double? populationA = null;
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == "prov_a" && resources[i].ResourceId == "population") {
						populationA = resources[i].Value;
					}
				}
			}

			Assert.Equal(1234.0, populationA);
		}

		[Fact]
		void province_population_growth_still_compounds_monthly() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Update(744f); // Jan1 -> Feb1 (31 days), crosses one month boundary
			double afterFirstMonth = GetResourceValue(logic.World, "prov_a", "population")!.Value;
			Assert.Equal(1234.0 * 1.00075, afterFirstMonth, 6);

			logic.Update(696f); // Feb1 -> Mar1 (1880 is a leap year: 29 days), crosses a second boundary
			double afterSecondMonth = GetResourceValue(logic.World, "prov_a", "population")!.Value;
			Assert.Equal(afterFirstMonth * 1.00075, afterSecondMonth, 6);
		}

		[Fact]
		void recruits_seeded_at_init_from_initial_percent_of_country_population() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(1234.0 * 0.05, GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value, 6);
			Assert.Equal(5678.0 * 0.05, GetResourceValue(logic.World, "France", "recruits")!.Value, 6);
		}

		[Fact]
		void recruits_grows_and_caps_across_multiple_months() {
			// PopulationGrowthPercentPerMonth = 0 keeps country_population exactly fixed at
			// 1234.0 across months, so cap/rawDelta arithmetic below is deterministic.
			var settings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly",
				PopulationGrowthPercentPerMonth = 0.0,
				RecruitsInitialPercent = 5.0,
				RecruitsCapPercent = 15.0,
				RecruitsMonthlyIncreasePercent = 1.0
			};
			var logic = BuildLogic(gameSettingsOverride: settings);
			logic.Update(0f); // seed: 5% of 1234 = 61.7

			double seeded = GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value;
			Assert.Equal(61.7, seeded, 4);

			const double cap = 1234.0 * 0.15;      // 185.1
			const double rawDelta = 1234.0 * 0.01; // 12.34

			// 32-day jumps always cross exactly one month boundary regardless of the
			// current day-of-month, so each call applies growth exactly once.
			logic.Update(768f);
			double afterFirstMonth = GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value;
			Assert.Equal(seeded + rawDelta, afterFirstMonth, 4);

			double current = afterFirstMonth;
			for (int i = 0; i < 9; i++) {
				logic.Update(768f);
				current = GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value;
			}
			// 61.7 + 12.34 * 10 == 185.1 exactly: growth should land precisely on the cap.
			Assert.Equal(cap, current, 4);

			logic.Update(768f); // one more month past the cap
			double afterCap = GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value;
			Assert.Equal(cap, afterCap, 4);
		}

		[Fact]
		void recruits_and_last_applied_delta_correct_immediately_after_load() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var settings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly",
				PopulationGrowthPercentPerMonth = 0.0,
				RecruitsInitialPercent = 5.0,
				RecruitsCapPercent = 15.0,
				RecruitsMonthlyIncreasePercent = 1.0
			};
			var logic = BuildLogic(storage, serializer, settings);
			logic.Update(0f);   // seed: 61.7
			logic.Update(768f); // one month of growth, well under cap: 74.04

			double recruitsBeforeSave = GetResourceValue(logic.World, "Great_Britain", "recruits")!.Value;
			double lastDeltaBeforeSave = GetEffectValue(logic.World, "Great_Britain", "recruits")!.Value;
			Assert.Equal(74.04, recruitsBeforeSave, 4);
			Assert.Equal(12.34, lastDeltaBeforeSave, 4);

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loadedLogic = BuildLogic(storage, serializer, settings);
			loadedLogic.LoadState(saveName);

			Assert.Equal(recruitsBeforeSave, GetResourceValue(loadedLogic.World, "Great_Britain", "recruits"));
			Assert.Equal(lastDeltaBeforeSave, GetEffectValue(loadedLogic.World, "Great_Britain", "recruits"));
		}

		[Fact]
		void make_friend_and_make_rival_never_populate_initial_hand_since_opinion_starts_at_zero() {
			const string targetRole = "diplomacy_advisor";
			var characterConfig = new CharacterConfig {
				Roles = new List<CharacterRoleDefinition> { new CharacterRoleDefinition { RoleId = targetRole } },
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "Great_Britain",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[targetRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "advisor_gb" } }
						}
					},
					new CountryCharacterPool {
						CountryId = "France",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[targetRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "advisor_fr" } }
						}
					}
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "make_friend",
						OwnerType = "country",
						TargetRole = targetRole,
						DeckCopies = 3,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 30 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					},
					new ActionDefinition {
						ActionId = "make_rival",
						OwnerType = "country",
						TargetRole = targetRole,
						DeckCopies = 3,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 30 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					}
				}
			};

			// Must not throw now that ExpressionContext.Opinion/.HasSuitableRelationTarget are
			// wired into CreateCountryActionEntities.
			var logic = BuildLogic(actionConfigOverride: actionConfig, characterConfigOverride: characterConfig);
			logic.Update(0f);

			int[] handReq = { TypeId<GameAction>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(handReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				for (int i = 0; i < arch.Count; i++) {
					Assert.NotEqual("make_friend", actions[i].ActionId);
					Assert.NotEqual("make_rival", actions[i].ActionId);
				}
			}
		}

		[Fact]
		void decrease_enemy_control_can_populate_initial_hand_when_another_org_has_control() {
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "decrease_enemy_control",
						OwnerType = "country",
						DeckCopies = 1,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gt",
								Members = new List<ExpressionNode> {
									new ExpressionNode {
										Type = "sub",
										Members = new List<ExpressionNode> {
											new ExpressionNode { Type = "totalCountryControl" },
											new ExpressionNode { Type = "control" }
										}
									},
									new ExpressionNode { Type = "value", Value = 0 }
								}
							}
						}
					}
				}
			};
			var organizationConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					},
					new OrganizationEntry {
						OrganizationId = "Masons",
						DisplayName = "Masons",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var logic = BuildLogic(
				actionConfigOverride: actionConfig,
				organizationConfigOverride: organizationConfig,
				participatingOrganizationIds: new[] { "Illuminati", "Masons" });

			logic.Update(0f);

			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CountryContext>.Value,
				TypeId<CardInHand>.Value
			};
			bool found = false;
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == "decrease_enemy_control"
						&& orgs[i].OrgId == "Illuminati"
						&& countries[i].CountryId == "Great_Britain") {
						found = true;
					}
				}
			}

			Assert.True(found);
		}

		[Fact]
		void diplomacy_and_military_gated_cards_resolve_opinion_independently_per_role_at_initial_hand_fill() {
			const string diplomacyRole = "diplomacy_advisor";
			const string militaryRole = "military_advisor";
			var characterConfig = new CharacterConfig {
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = diplomacyRole },
					new CharacterRoleDefinition { RoleId = militaryRole }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "Great_Britain",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[diplomacyRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "diplo_gb" } },
							[militaryRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "mil_gb" } }
						}
					},
					new CountryCharacterPool {
						CountryId = "France",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[diplomacyRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "diplo_fr" } },
							[militaryRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = "mil_fr" } }
						}
					}
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "make_friend",
						OwnerType = "country",
						TargetRole = diplomacyRole,
						DeckCopies = 1,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 30 }
								}
							}
						}
					},
					new ActionDefinition {
						ActionId = "ultimatum",
						OwnerType = "country",
						TargetRole = militaryRole,
						DeckCopies = 1,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "control" },
									new ExpressionNode { Type = "value", Value = 10 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 50 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "isInWar" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "warProgress" },
									new ExpressionNode { Type = "value", Value = 50 }
								}
							}
						}
					}
				}
			};
			var logic = BuildLogic(actionConfigOverride: actionConfig, characterConfigOverride: characterConfig);

			// Seed both advisors' opinion before init runs: diplomacy advisor satisfies make_friend's
			// gate, military advisor does not satisfy ultimatum's — proving the two cards' own-role
			// opinion is resolved independently rather than off one shared value.
			int diploResEntity = logic.World.Create();
			logic.World.Add(diploResEntity, new ResourceOwner("diplo_gb", OwnerType.Character));
			logic.World.Add(diploResEntity, new Resource { ResourceId = "opinion_Illuminati", Value = 50 });
			int milResEntity = logic.World.Create();
			logic.World.Add(milResEntity, new ResourceOwner("mil_gb", OwnerType.Character));
			logic.World.Add(milResEntity, new Resource { ResourceId = "opinion_Illuminati", Value = 10 });
			Wars.DeclareWar(logic.World, "Great_Britain", "France", new DateTime(1880, 1, 1));
			int[] warReq = { TypeId<War>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(warReq, null)) {
				var wars = arch.GetColumn<War>();
				for (int i = 0; i < arch.Count; i++) {
					ResourceMutations.TrySetValue(logic.World, wars[i].WarId, ResourceDefinitions.WarProgress, 50, out _);
				}
			}

			logic.Update(0f);

			bool foundMakeFriend = false;
			bool foundUltimatum = false;
			int[] handReq = { TypeId<GameAction>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(handReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (countries[i].CountryId != "Great_Britain") { continue; }
					if (actions[i].ActionId == "make_friend") { foundMakeFriend = true; }
					if (actions[i].ActionId == "ultimatum") { foundUltimatum = true; }
				}
			}

			Assert.True(foundMakeFriend);
			Assert.False(foundUltimatum);
		}
	}
}
