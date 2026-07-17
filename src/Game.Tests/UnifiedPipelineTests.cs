using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class UnifiedPipelineTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string HqCountryId = "Great_Britain";
		const string OtherCountryId = "France";

		static GameLogic BuildLogic(ActionConfig actionConfig, EffectConfig effectConfig, CharacterConfig? characterConfig = null) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
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
				initialPlayerCountryId: HqCountryId,
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
		void pipeline_discovers_country_on_org_action_success() {
			var actionConfig = SingleOrgActionConfig("spread_rumors", 0.0, new List<string> { "discover" });
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};
			var logic = BuildLogic(actionConfig, effectConfig);
			logic.Update(0f);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, ActionId = "spread_rumors" });
			logic.Update(0f);

			bool discovered = false;
			int[] discReq = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(discReq, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == OrgId && dcs[i].CountryId == OtherCountryId) { discovered = true; }
				}
			}
			Assert.True(discovered);
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
			var actionConfig = SingleOrgActionConfig("spread_rumors", 0.0, new List<string> { "discover" });
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};
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

			int[] discoverReq = { TypeId<DiscoverCountryEffect>.Value };
			int discoverCount = 0;
			foreach (var arch in logic.World.GetMatchingArchetypes(discoverReq, null)) { discoverCount += arch.Count; }
			Assert.Equal(0, discoverCount);
		}
	}
}
