using System;
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
	// Covers Docs/Specs/26_07_18_07_action-log-ui/plan.md — the Action Log feature's
	// UpdateGameLog collection logic. Follows the ControlFeatureTests /
	// CharacterVisualStateTests convention: bespoke GameLogicContext/ActionConfig/EffectConfig
	// per scenario, GameLogic driven directly via Update(...) and Commands.Push(...).
	public class GameLogStateTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string OrgBId = "Masons";
		const string OrgCId = "Rosicrucians";
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
				AutoSaveInterval = "monthly",
				FeatureFlags = new FeatureFlagSettings { EnableRuler = true }
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition { ResourceId = ResourceDefinitions.WarInitiative, SeedTarget = ResourceSeedTarget.Country }
				}
			};

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

		// DeckCopies = 0 so InitSystem creates no per-target make_friend instances automatically —
		// tests using this config seed their own single RelationCardTarget-bearing instance
		// directly into hand, for a deterministic outcome independent of which of the ~N per-target
		// instances an initial random draw would otherwise pick.
		static ActionConfig RelationActionConfig() => new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition { ActionId = "make_friend", OwnerType = "country", DeckCopies = 0, EffectIds = new List<string> { "make_friend_effect" } }
			}
		};

		static EffectConfig RelationEffectConfig() => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new SetCountryRelationEffectParams { EffectId = "make_friend_effect", EffectType = "SetCountryRelation", Kind = RelationKind.Friend }
			}
		};

		const string MilitaryAdvisorId = "france_military_advisor";

		static ExpressionNode Gte(string type, double value) => new ExpressionNode {
			Type = "gte",
			Members = new List<ExpressionNode> {
				new ExpressionNode { Type = type },
				new ExpressionNode { Type = "value", Value = value }
			}
		};

		static ExpressionNode Lte(string type, double value) => new ExpressionNode {
			Type = "lte",
			Members = new List<ExpressionNode> {
				new ExpressionNode { Type = type },
				new ExpressionNode { Type = "value", Value = value }
			}
		};

		static ActionConfig WarResolutionActionConfig() => new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition {
					ActionId = "force_war_win",
					OwnerType = "country",
					TargetRole = "military_advisor",
					DeckCopies = 1,
					Conditions = new List<ExpressionNode> {
						Gte("control", 10),
						Gte("opinion", 50),
						Gte("isInWar", 1),
						Gte("warProgress", 50)
					},
					Cost = new List<ActionCost> {
						new ActionCost { ResourceId = "gold", Amount = 300.0 }
					},
					EffectIds = new List<string> { "force_war_win_effect" }
				},
				new ActionDefinition {
					ActionId = "force_war_loss",
					OwnerType = "country",
					TargetRole = "military_advisor",
					DeckCopies = 1,
					Conditions = new List<ExpressionNode> {
						Gte("control", 20),
						Gte("opinion", 80),
						Gte("isInWar", 1),
						Lte("warProgress", 0)
					},
					Cost = new List<ActionCost> {
						new ActionCost { ResourceId = "gold", Amount = 500.0 }
					},
					EffectIds = new List<string> { "force_war_loss_effect" }
				}
			}
		};

		static EffectConfig WarResolutionEffectConfig() => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new ResolveWarEffectParams {
					EffectId = "force_war_win_effect",
					EffectType = "ResolveWar",
					Outcome = WarOutcome.Win
				},
				new ResolveWarEffectParams {
					EffectId = "force_war_loss_effect",
					EffectType = "ResolveWar",
					Outcome = WarOutcome.Lose
				}
			}
		};

		static CharacterConfig WarResolutionCharacterConfig() => new CharacterConfig {
			Roles = new List<CharacterRoleDefinition> {
				new CharacterRoleDefinition { RoleId = "military_advisor" }
			},
			CountryPools = new List<CountryCharacterPool> {
				new CountryCharacterPool {
					CountryId = OtherCountryId,
					Slots = new Dictionary<string, List<CharacterEntry>> {
						["military_advisor"] = new List<CharacterEntry> {
							new CharacterEntry { CharacterId = MilitaryAdvisorId }
						}
					}
				}
			}
		};

		static GameLogic BuildWarResolutionLogic(double rawWarProgress = -60) {
			var logic = BuildLogic(
				WarResolutionActionConfig(),
				WarResolutionEffectConfig(),
				WarResolutionCharacterConfig());
			logic.Update(0f);

			AddControl(logic.World, OrgId, OtherCountryId, 20, "war_resolution_control");

			int opinionEntity = logic.World.Create();
			logic.World.Add(opinionEntity, new ResourceOwner(MilitaryAdvisorId, OwnerType.Character));
			logic.World.Add(opinionEntity, new Resource {
				ResourceId = $"opinion_{OrgId}",
				Value = 80
			});

			Assert.True(Wars.DeclareWar(logic.World, logic.Resources, HqCountryId, OtherCountryId, new DateTime(1880, 1, 1)));
			int[] warRequired = { TypeId<War>.Value };
			foreach (Archetype archetype in logic.World.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = archetype.GetColumn<War>();
				for (int i = 0; i < archetype.Count; i++) {
					ResourceMutations.TrySetValue(logic.Resources, logic.World, wars[i].WarId, ResourceDefinitions.WarProgress, rawWarProgress, out _);
				}
			}

			return logic;
		}

		// Covers the Relation game-log/fly-text wiring: RelationSetApplied -> GameLogEntryKind.Relation.
		// make_friend/make_rival cards are now per-target instances (RelationCardTarget, no
		// CountryContext — the primary side stays dynamic), so this seeds a single instance
		// directly into hand rather than relying on which of the ~N per-target instances an
		// initial random draw would pick — see RelationActionConfig's DeckCopies = 0.
		[Fact]
		void relation_produces_exactly_one_entry_with_target_and_kind_and_no_extra_on_a_passive_tick() {
			var logic = BuildLogic(RelationActionConfig(), RelationEffectConfig());
			logic.Update(0f);

			int cardEntity = logic.World.Create();
			logic.World.Add(cardEntity, new GameAction { ActionId = "make_friend" });
			logic.World.Add(cardEntity, new OrgContext { OrgId = OrgId });
			logic.World.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			logic.World.Add(cardEntity, new RelationCardTarget { TargetCountryId = HqCountryId, Kind = RelationKind.Friend });
			logic.World.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "make_friend",
				TargetCountryId = HqCountryId, SlotIndex = 0
			});
			logic.Update(0f);

			var relations = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Relation).ToList();
			Assert.Single(relations);
			Assert.Equal(OrgId, relations[0].OrgId);
			Assert.Equal(OtherCountryId, relations[0].CountryId);
			Assert.Equal(HqCountryId, relations[0].TargetCountryId);
			Assert.Equal(RelationKind.Friend, relations[0].RelationKind);

			// Passive tick, no new PlayCardActionCommand — no additional entry, confirming
			// RelationSetApplied was swept by CleanupEffectNotificationsSystem like the other *Applied events.
			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Relation));
		}

		// Defends CreateActionEffectSystem's SetCountryRelationEffectParams guard: a make_friend-shaped
		// card entity with no RelationCardTarget (not reachable in production after this feature — every
		// make_friend/make_rival instance InitSystem creates always carries one) must create no
		// SetCountryRelationEffect marker at all, rather than falling back to any implicit target.
		[Fact]
		void set_country_relation_effect_is_not_created_without_a_relation_card_target() {
			var logic = BuildLogic(RelationActionConfig(), RelationEffectConfig());
			logic.Update(0f);

			int cardEntity = logic.World.Create();
			logic.World.Add(cardEntity, new GameAction { ActionId = "make_friend" });
			logic.World.Add(cardEntity, new OrgContext { OrgId = OrgId });
			logic.World.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			logic.World.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "make_friend", SlotIndex = 0
			});
			logic.Update(0f);

			Assert.Empty(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Relation));
			Assert.Null(logic.Relations.GetRelation(logic.World, OtherCountryId, HqCountryId));
		}

		[Fact]
		void ultimatum_resolves_war_with_selected_country_as_winner_and_logs_exactly_once() {
			var logic = BuildWarResolutionLogic();
			PutCountryCardInHand(logic.World, OrgId, OtherCountryId, "force_war_win");

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId,
				CountryId = OtherCountryId,
				ActionId = "force_war_win"
			});
			logic.Update(0f);

			GameLogEntry warResolution = Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.WarResolved));
			Assert.Equal(OtherCountryId, warResolution.CountryId);
			Assert.Equal(HqCountryId, warResolution.TargetCountryId);
			Assert.False(Wars.IsInWar(logic.World, HqCountryId));
			Assert.False(Wars.IsInWar(logic.World, OtherCountryId));
			Assert.Equal(700, _resources.GetValue(logic.World, OrgId, "gold"));

			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.WarResolved));
		}

		[Fact]
		void surrender_resolves_war_with_selected_country_as_loser_and_logs_swapped_outcome() {
			// OtherCountryId is the defender, so raw +60 means its own war progress is -60 (losing).
			var logic = BuildWarResolutionLogic(rawWarProgress: 60);
			PutCountryCardInHand(logic.World, OrgId, OtherCountryId, "force_war_loss");

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId,
				CountryId = OtherCountryId,
				ActionId = "force_war_loss"
			});
			logic.Update(0f);

			GameLogEntry warResolution = Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.WarResolved));
			Assert.Equal(HqCountryId, warResolution.CountryId);
			Assert.Equal(OtherCountryId, warResolution.TargetCountryId);
			Assert.False(Wars.IsInWar(logic.World, HqCountryId));
			Assert.False(Wars.IsInWar(logic.World, OtherCountryId));
			Assert.Equal(500, _resources.GetValue(logic.World, OrgId, "gold"));

			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.WarResolved));
		}

		static ActionConfig ControlActionConfig(int deckCopies) => new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "country", HandSize = deckCopies }
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition { ActionId = "raise_control", OwnerType = "country", DeckCopies = deckCopies, EffectIds = new List<string> { "control_gain" } }
			}
		};

		// Distinct ActionIds sharing the same "control_gain" effect — used where a test needs
		// several successive control-raising plays by the *same* org in the same tick-sequence.
		// Reusing a single ActionId for that would now trip the (OrgId, ActionId) cooldown gate
		// added in Docs/Specs/26_08_04_17_card-cooldown/plan.md, which is unrelated to what these
		// tests are actually verifying (GameLog entry independence / eviction).
		static ActionConfig MultiControlActionConfig(params string[] actionIds) {
			var config = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = actionIds.Length }
				}
			};
			foreach (string actionId in actionIds) {
				config.Actions.Add(new ActionDefinition {
					ActionId = actionId, OwnerType = "country", DeckCopies = 1, EffectIds = new List<string> { "control_gain" }
				});
			}
			return config;
		}

		static EffectConfig ControlEffectConfig(int amount) => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new ControlChangeEffectParams { EffectId = "control_gain", EffectType = "ControlChange", Amount = amount }
			}
		};

		static ActionConfig DecreaseEnemyControlActionConfig() => new ActionConfig {
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
					},
					Cost = new List<ActionCost> {
						new ActionCost { ResourceId = "gold", Amount = 250.0 }
					},
					EffectIds = new List<string> { "enemy_drain", "control_gain" }
				}
			}
		};

		static EffectConfig DecreaseEnemyControlEffectConfig() => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new EnemyControlDrainEffectParams { EffectId = "enemy_drain", EffectType = "EnemyControlDrain", Amount = 20 },
				new ControlChangeEffectParams { EffectId = "control_gain", EffectType = "ControlChange", Amount = 10 }
			}
		};

		static int AddControl(World world, string orgId, string countryId, int value, string effectId) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = effectId
			});
			return entity;
		}

		static int GetControl(World world, string orgId, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (controls[i].OrgId == orgId && controls[i].CountryId == countryId) {
						total += controls[i].Value;
					}
				}
			}
			return total;
		}

		static void PutCountryCardInHand(World world, string orgId, string countryId, string actionId) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId
						&& orgs[i].OrgId == orgId
						&& owners[i].Value == CardOwnerKind.Country) {
						int entity = arch.Entities[i];
						if (!world.Has<CardInHand>(entity)) {
							world.Add(entity, new CardInHand { SlotIndex = 0 });
						}
						return;
					}
				}
			}
			throw new InvalidOperationException($"Card not found: org={orgId} country={countryId} action={actionId}");
		}

		static int FindCountryCardSlot(World world, string orgId, string actionId) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId
						&& orgs[i].OrgId == orgId
						&& owners[i].Value == CardOwnerKind.Country) {
						return hands[i].SlotIndex;
					}
				}
			}
			throw new InvalidOperationException($"Country hand card not found: org={orgId} action={actionId}");
		}

		[Fact]
		void control_entries_carry_independent_delta_and_running_total() {
			// Two distinct ActionIds (not the same one twice) — the same org playing the same
			// country ActionId back-to-back would now be blocked by the card-cooldown gate; this
			// test is about GameLog entry independence, not cooldown, so it plays two different
			// control-raising cards instead. See MultiControlActionConfig.
			var logic = BuildLogic(MultiControlActionConfig("raise_control_1", "raise_control_2"), ControlEffectConfig(5));
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control_1",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "raise_control_1")
			});
			logic.Update(0f);
			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Single(controls);
			Assert.Equal(5, controls[0].Delta);
			Assert.Equal(5, controls[0].Total);

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control_2",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "raise_control_2")
			});
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

		[Fact]
		void decrease_enemy_control_logs_clamped_drain_and_gain() {
			var logic = BuildLogic(DecreaseEnemyControlActionConfig(), DecreaseEnemyControlEffectConfig());
			logic.Update(0f);
			AddControl(logic.World, OrgBId, OtherCountryId, 15, "enemy");
			PutCountryCardInHand(logic.World, OrgId, OtherCountryId, "decrease_enemy_control");

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId,
				CountryId = OtherCountryId,
				ActionId = "decrease_enemy_control"
			});
			logic.Update(0f);

			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Equal(2, controls.Count);
			var drain = controls.Single(e => e.OrgId == OrgBId);
			var gain = controls.Single(e => e.OrgId == OrgId);
			Assert.Equal(-15, drain.Delta);
			Assert.Equal(0, drain.Total);
			Assert.Equal(10, gain.Delta);
			Assert.Equal(10, gain.Total);
			Assert.Equal(0, GetControl(logic.World, OrgBId, OtherCountryId));
			Assert.Equal(10, GetControl(logic.World, OrgId, OtherCountryId));
		}

		[Fact]
		void decrease_enemy_control_targets_only_ordinally_first_org_on_a_tie() {
			var logic = BuildLogic(DecreaseEnemyControlActionConfig(), DecreaseEnemyControlEffectConfig());
			logic.Update(0f);
			AddControl(logic.World, OrgCId, OtherCountryId, 30, "enemy_c");
			AddControl(logic.World, OrgBId, OtherCountryId, 30, "enemy_b");
			PutCountryCardInHand(logic.World, OrgId, OtherCountryId, "decrease_enemy_control");

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId,
				CountryId = OtherCountryId,
				ActionId = "decrease_enemy_control"
			});
			logic.Update(0f);

			Assert.Equal(10, GetControl(logic.World, OrgBId, OtherCountryId));
			Assert.Equal(30, GetControl(logic.World, OrgCId, OtherCountryId));
			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Single(controls.Where(e => e.OrgId == OrgBId && e.Delta == -20));
			Assert.Empty(controls.Where(e => e.OrgId == OrgCId));
		}

		[Fact]
		void decrease_enemy_control_gain_uses_post_drain_pool_capacity() {
			var logic = BuildLogic(DecreaseEnemyControlActionConfig(), DecreaseEnemyControlEffectConfig());
			logic.Update(0f);
			AddControl(logic.World, OrgId, OtherCountryId, 95, "own");
			AddControl(logic.World, OrgBId, OtherCountryId, 5, "enemy");
			PutCountryCardInHand(logic.World, OrgId, OtherCountryId, "decrease_enemy_control");

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId,
				CountryId = OtherCountryId,
				ActionId = "decrease_enemy_control"
			});
			logic.Update(0f);

			Assert.Equal(0, GetControl(logic.World, OrgBId, OtherCountryId));
			Assert.Equal(100, GetControl(logic.World, OrgId, OtherCountryId));
			var controls = Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control).ToList();
			Assert.Single(controls.Where(e => e.OrgId == OrgBId && e.Delta == -5 && e.Total == 0));
			Assert.Single(controls.Where(e => e.OrgId == OrgId && e.Delta == 5 && e.Total == 100));
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

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "improve_opinion",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "improve_opinion")
			});
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

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = OtherCountryId, ActionId = "improve_opinion",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "improve_opinion")
			});
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
			var gameSettings = new GameSettings {
				StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly",
				GameLog = new GameLogSettings { IncludePlayerActions = false, MaxLogEntries = 12 }
			};
			var characterConfig = NewCharacterConfig();
			var logic = BuildLogic(ControlActionConfig(1), ControlEffectConfig(5), characterConfig, gameSettings, orgConfig, countryConfig,
				participatingOrgIds: new List<string> { OrgId, OrgBId });
			logic.Update(0f);

			// Player org (Illuminati, the initialOrganizationId) control — suppressed.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);
			Assert.Empty(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control));

			// AI org control — still appears.
			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgBId, CountryId = OtherCountryId, ActionId = "raise_control" });
			logic.Update(0f);
			Assert.Single(Entries(logic).Where(e => e.Kind == GameLogEntryKind.Control && e.OrgId == OrgBId));

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
			// Three distinct ActionIds — the same org replaying the same country ActionId
			// back-to-back would now be blocked by the card-cooldown gate; this test is about
			// GameLog eviction, not cooldown, so each play uses its own control-raising card.
			// See MultiControlActionConfig.
			var actionConfig = MultiControlActionConfig("raise_control_a", "raise_control_b", "raise_control_c");
			var gameSettings = new GameSettings {
				StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly",
				GameLog = new GameLogSettings { IncludePlayerActions = true, MaxLogEntries = 2 }
			};
			var logic = BuildLogic(actionConfig, ControlEffectConfig(5), gameSettings: gameSettings, countryConfig: countryConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = CountryA, ActionId = "raise_control_a",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "raise_control_a")
			});
			logic.Update(0f);
			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = CountryB, ActionId = "raise_control_b",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "raise_control_b")
			});
			logic.Update(0f);
			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = CountryC, ActionId = "raise_control_c",
				SlotIndex = FindCountryCardSlot(logic.World, OrgId, "raise_control_c")
			});
			logic.Update(0f);

			var entries = Entries(logic);
			Assert.Equal(2, entries.Count);
			Assert.Equal(CountryB, entries[0].CountryId);
			Assert.Equal(CountryC, entries[1].CountryId);
		}
	}
}
