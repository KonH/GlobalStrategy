using System;
using System.Collections.Generic;
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
	public class UnifiedPipelineTests {
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string HqCountryId = "Great_Britain";
		const string OtherCountryId = "France";

		static GameLogic BuildLogic(
			ActionConfig actionConfig, EffectConfig effectConfig, CharacterConfig? characterConfig = null,
			CountryConfig? countryConfig = null, OrganizationConfig? orgConfig = null) {
			countryConfig ??= new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
				}
			};
			orgConfig ??= new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = OrgId,
						DisplayName = "Illuminati",
						HqCountryId = HqCountryId,
						InitialGold = 1000.0
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
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: OrgId,
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(effectConfig),
				character: characterConfig != null ? new StaticConfig<CharacterConfig>(characterConfig) : null);
			return new GameLogic(ctx);
		}

		static ActionConfig SingleOrgActionConfig(string actionId, double goldCost, List<string> effectIds) {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = OrgId, ActionIds = new List<string> { actionId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = actionId,
						OwnerType = "org",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = goldCost } },
						EffectIds = effectIds
					}
				}
			};
		}

		static int FindOrgResourceEntity(World world, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == OrgId && resources[i].ResourceId == resourceId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}

		static int FindCardEntity(World world, string actionId, bool requireCardUse = false) {
			int[] req = requireCardUse
				? new[] { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value }
				: new[] { TypeId<GameAction>.Value, TypeId<OrgContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId) { return arch.Entities[i]; }
				}
			}
			return -1;
		}

		[Fact]
		void pipeline_deducts_cost_on_valid_org_action() {
			var actionConfig = SingleOrgActionConfig("spread_rumors", 100.0, new List<string>());
			var effectConfig = new EffectConfig();
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);

			int goldEntity = FindOrgResourceEntity(logic.World, "gold");
			Assert.Equal(900.0, logic.World.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void pipeline_does_not_execute_when_insufficient_gold() {
			var actionConfig = SingleOrgActionConfig("expensive_action", 10000.0, new List<string>());
			var effectConfig = new EffectConfig();
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "expensive_action" });
			logic.Update(0f);

			int goldEntity = FindOrgResourceEntity(logic.World, "gold");
			Assert.Equal(1000.0, logic.World.Get<Resource>(goldEntity).Value);

			int cardEntity = FindCardEntity(logic.World, "expensive_action", requireCardUse: true);
			Assert.NotEqual(-1, cardEntity);
			Assert.False(logic.World.Has<ActionSucceeded>(cardEntity));
		}

		[Fact]
		void pipeline_card_action_always_succeeds() {
			var actionConfig = SingleOrgActionConfig("spread_rumors", 0.0, new List<string>());
			var effectConfig = new EffectConfig();
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);

			int cardEntity = FindCardEntity(logic.World, "spread_rumors", requireCardUse: true);
			Assert.NotEqual(-1, cardEntity);
			Assert.True(logic.World.Has<ActionSucceeded>(cardEntity));
		}

		[Fact]
		void pipeline_draws_replacement_card_after_play() {
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = OrgId, ActionIds = new List<string> { "action_a", "action_b" } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "action_a", OwnerType = "org" },
					new ActionDefinition { ActionId = "action_b", OwnerType = "org" }
				}
			};
			var effectConfig = new EffectConfig();
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			string? inHandActionId = null;
			int[] handReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			int[] excludeCountry = { TypeId<CountryContext>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(handReq, excludeCountry)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				for (int i = 0; i < arch.Count; i++) { inHandActionId = actions[i].ActionId; }
			}
			Assert.NotNull(inHandActionId);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = inHandActionId! });
			logic.Update(0f);

			int handCount = 0;
			foreach (var arch in logic.World.GetMatchingArchetypes(handReq, excludeCountry)) {
				handCount += arch.Count;
			}
			Assert.Equal(1, handCount);
		}

		[Fact]
		void pipeline_country_action_adds_control_on_success() {
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "build_influence",
						OwnerType = "country",
						DeckCopies = 1,
						EffectIds = new List<string> { "control_gain" }
					}
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "control_gain", EffectType = "ControlChange", Amount = 10 }
				}
			};
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "build_influence" });
			logic.Update(0f);

			bool found = false;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].OrgId == OrgId && effects[i].CountryId == OtherCountryId && effects[i].Value == 10) {
						found = true;
					}
				}
			}
			Assert.True(found);
		}

		[Fact]
		void pipeline_country_action_adds_opinion_resource_on_success() {
			const string targetRole = "ruler";
			const string charId = "napoleon";

			var characterConfig = new CharacterConfig {
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = targetRole }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = OtherCountryId,
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[targetRole] = new List<CharacterEntry> {
								new CharacterEntry { CharacterId = charId }
							}
						}
					}
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "improve_opinion",
						OwnerType = "country",
						TargetRole = targetRole,
						DeckCopies = 1,
						EffectIds = new List<string> { "opinion_boost" }
					}
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new OpinionModifierEffectParams {
						EffectId = "opinion_boost",
						EffectType = "OpinionModifier",
						InitialValue = 20,
						DecayPerMonth = 5
					}
				}
			};
			var logic = BuildLogic(actionConfig, effectConfig, characterConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = OtherCountryId, ActionId = "improve_opinion" });
			logic.Update(0f);

			string opinionResourceId = $"opinion_{OrgId}";
			double? opinionValue = null;
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == charId && owners[i].OwnerType == OwnerType.Character && resources[i].ResourceId == opinionResourceId) {
						opinionValue = resources[i].Value;
					}
				}
			}
			Assert.Equal(20.0, opinionValue);
		}

		[Fact]
		void cleanup_system_removes_prior_frame_components() {
			var actionConfig = SingleOrgActionConfig("spread_rumors", 0.0, new List<string>());
			var effectConfig = new EffectConfig();
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);

			int cardEntity = FindCardEntity(logic.World, "spread_rumors");
			Assert.NotEqual(-1, cardEntity);
			Assert.True(logic.World.Has<ActionSucceeded>(cardEntity));
			Assert.True(logic.World.Has<CardUse>(cardEntity));

			// Next frame with no new commands: cleanup should wipe transient execution components,
			// while GameAction (persistent identity) remains.
			logic.Update(0f);

			Assert.True(logic.World.Has<GameAction>(cardEntity));
			Assert.False(logic.World.Has<ActionSucceeded>(cardEntity));
			Assert.False(logic.World.Has<CardUse>(cardEntity));
			Assert.False(logic.World.Has<ActionValid>(cardEntity));
		}

		// Covers Docs/Specs/26_07_24_13_stop-friendship-rivalry-cards/plan.md's Tests-section
		// end-to-end acceptance case: two simultaneous stop_friendship instances (naming France and
		// Germany) sit in the same country's hand at once. Playing the France instance must clear
		// only the France relation, leaving the Germany instance's own relation and playability
		// completely untouched — proving InitActionFromPlayCardSystem's TargetCountryId
		// disambiguation (not just "first same-ActionId match wins") actually drives which entity
		// gets used/cleared.
		[Fact]
		void playing_one_of_two_simultaneous_stop_friendship_instances_clears_only_its_own_named_relation() {
			const string SelectedCountryId = "Prussia";
			const string GermanyId = "Germany";
			const string DiplomacyAdvisorId = "prussia_diplomat";
			const string TargetRole = "diplomacy_advisor";

			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = SelectedCountryId, DisplayName = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true },
					new CountryEntry { CountryId = GermanyId, DisplayName = "Germany", IsAvailable = true }
				}
			};
			var characterConfig = new CharacterConfig {
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = TargetRole }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = SelectedCountryId,
						Slots = new Dictionary<string, List<CharacterEntry>> {
							[TargetRole] = new List<CharacterEntry> { new CharacterEntry { CharacterId = DiplomacyAdvisorId } }
						}
					}
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 2 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "stop_friendship",
						OwnerType = "country",
						TargetRole = TargetRole,
						DeckCopies = 0,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> { new ExpressionNode { Type = "opinion" }, new ExpressionNode { Type = "value", Value = 80 } }
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> { new ExpressionNode { Type = "relationStillExists" }, new ExpressionNode { Type = "value", Value = 1 } }
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 100.0 } },
						EffectIds = new List<string> { "clear_friendship_effect" }
					}
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ClearCountryRelationEffectParams { EffectId = "clear_friendship_effect", EffectType = "ClearCountryRelation", Kind = RelationKind.Friend }
				}
			};

			var logic = BuildLogic(actionConfig, effectConfig, characterConfig, countryConfig);
			logic.Update(0f);

			// Opinion gate: seed the diplomacy advisor's opinion resource straight to the >=80 threshold.
			int opinionEntity = logic.World.Create();
			logic.World.Add(opinionEntity, new ResourceOwner(DiplomacyAdvisorId, OwnerType.Character));
			logic.World.Add(opinionEntity, new Resource { ResourceId = $"opinion_{OrgId}", Value = 80.0 });

			CountryRelations.SetRelation(logic.World, SelectedCountryId, OtherCountryId, RelationKind.Friend);
			CountryRelations.SetRelation(logic.World, SelectedCountryId, GermanyId, RelationKind.Friend);

			// Seed both instances directly into hand rather than relying on RelationCardSyncSystem +
			// random draw luck — see plan Tests section / spec.md acceptance criterion.
			int franceCard = logic.World.Create();
			logic.World.Add(franceCard, new GameAction { ActionId = "stop_friendship" });
			logic.World.Add(franceCard, new OrgContext { OrgId = OrgId });
			logic.World.Add(franceCard, new CountryContext { CountryId = SelectedCountryId });
			logic.World.Add(franceCard, new CardInHand { SlotIndex = 0 });
			logic.World.Add(franceCard, new RelationCardTarget { TargetCountryId = OtherCountryId, Kind = RelationKind.Friend });

			int germanyCard = logic.World.Create();
			logic.World.Add(germanyCard, new GameAction { ActionId = "stop_friendship" });
			logic.World.Add(germanyCard, new OrgContext { OrgId = OrgId });
			logic.World.Add(germanyCard, new CountryContext { CountryId = SelectedCountryId });
			logic.World.Add(germanyCard, new CardInHand { SlotIndex = 1 });
			logic.World.Add(germanyCard, new RelationCardTarget { TargetCountryId = GermanyId, Kind = RelationKind.Friend });

			logic.Commands.Push(new PlayCardActionCommand {
				OrgId = OrgId, CountryId = SelectedCountryId, ActionId = "stop_friendship", TargetCountryId = OtherCountryId
			});
			logic.Update(0f);

			// The France instance played and succeeded; only the France relation was cleared.
			Assert.True(logic.World.Has<ActionSucceeded>(franceCard));
			Assert.Null(CountryRelations.GetRelation(logic.World, SelectedCountryId, OtherCountryId));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, SelectedCountryId, GermanyId));

			// The Germany instance was never touched by InitActionFromPlayCardSystem's matching —
			// it must not have been marked used and must still be in hand (proves this isn't a
			// false-positive from a fixture that merely never looked at the Germany entity).
			Assert.False(logic.World.Has<CardUse>(germanyCard));
			Assert.True(logic.World.Has<CardInHand>(germanyCard));

			// It is, however, now unplayable: playing the France instance starts a shared
			// (OrgId, "stop_friendship") cooldown that covers every instance of that ActionId for
			// the org — including this untouched Germany copy — per
			// Docs/Specs/26_08_04_17_card-cooldown/spec.md. It becomes playable again once the
			// cooldown elapses.
			var currentTime = new DateTime(1880, 1, 1);
			Assert.False(ActionPlayability.Evaluate(logic.World, actionConfig, germanyCard, "stop_friendship", OrgId, SelectedCountryId, currentTime: currentTime));
			Assert.True(ActionPlayability.Evaluate(logic.World, actionConfig, germanyCard, "stop_friendship", OrgId, SelectedCountryId, currentTime: currentTime.AddDays(8)));

			// Exactly one RelationClearedApplied event, naming France — not a second one for Germany.
			var clearedEvents = new List<RelationClearedApplied>();
			int[] clearedReq = { TypeId<RelationClearedApplied>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(clearedReq, null)) {
				RelationClearedApplied[] events = arch.GetColumn<RelationClearedApplied>();
				for (int i = 0; i < arch.Count; i++) { clearedEvents.Add(events[i]); }
			}
			Assert.Single(clearedEvents);
			Assert.Equal(OtherCountryId, clearedEvents[0].TargetCountryId);
		}
	}
}
