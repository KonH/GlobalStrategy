using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class OrgDestroySystemTests {
		const string OrgId = "org-a";
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		[Fact]
		void positive_control_is_a_hard_destroy_gate() {
			var world = BuildWorld(out int orgEntity);
			AddControl(world, 1);

			bool destroyed = TryDestroy(world, orgEntity, new ActionConfig(), new EffectConfig());

			Assert.False(destroyed);
			Assert.False(world.Has<IsOrgDestroyed>(orgEntity));
		}

		[Fact]
		void affordable_discard_and_non_full_hand_each_prevent_destroy() {
			var affordableWorld = BuildWorld(out int affordableOrg);
			AddGold(affordableWorld, 50);
			Assert.False(TryDestroy(affordableWorld, affordableOrg, new ActionConfig(), new EffectConfig()));

			var nonFullWorld = BuildWorld(out int nonFullOrg);
			AddDeck(nonFullWorld, CardOwnerKind.Country, handSize: 1);
			Assert.False(TryDestroy(nonFullWorld, nonFullOrg, new ActionConfig(), new EffectConfig()));
		}

		[Fact]
		void a_control_raising_card_in_either_pool_prevents_destroy() {
			foreach (CardOwnerKind ownerKind in new[] { CardOwnerKind.Country, CardOwnerKind.Org }) {
				var world = BuildWorld(out int orgEntity);
				AddDeck(world, ownerKind, handSize: 1);
				AddCard(world, ownerKind, "raise-control");
				var actions = new ActionConfig {
					Actions = new List<ActionDefinition> {
						new ActionDefinition {
							ActionId = "raise-control",
							EffectIds = new List<string> { "raise" }
						}
					}
				};
				var effects = new EffectConfig {
					Effects = new List<ActionEffectDefinition> {
						new ControlChangeEffectParams { EffectId = "raise", Amount = 1 }
					}
				};

				Assert.False(TryDestroy(world, orgEntity, actions, effects));
			}
		}

		[Fact]
		void all_conditions_add_one_flag_and_event_flip_outcome_and_purge_residual_control() {
			var world = BuildWorld(out int orgEntity);
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "too-expensive");
			AddControl(world, -1);
			var actions = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "too-expensive",
						Cost = new List<ActionCost> {
							new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 100 }
						}
					}
				}
			};

			Assert.True(TryDestroy(world, orgEntity, actions, new EffectConfig()));
			Assert.True(world.Has<IsOrgDestroyed>(orgEntity));
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(orgEntity).Result);
			Assert.Equal(0, Count<ControlEffect>(world));
			Assert.Equal(1, Count<OrgDestroyedApplied>(world));

			Assert.False(TryDestroy(world, orgEntity, actions, new EffectConfig()));
			Assert.Equal(1, Count<OrgDestroyedApplied>(world));
		}

		[Fact]
		void cooldown_as_the_only_failure_counts_as_playable_and_prevents_destroy() {
			var world = BuildWorld(out int orgEntity);
			AddDeck(world, CardOwnerKind.Org, handSize: 1);
			AddCard(world, CardOwnerKind.Org, "cooldown-card");
			int cooldown = world.Create();
			world.Add(cooldown, new ActionCooldownState {
				OrgId = OrgId,
				ActionId = "cooldown-card",
				EndTime = new System.DateTime(2100, 1, 1)
			});
			var actions = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "cooldown-card" }
				}
			};

			Assert.False(TryDestroy(world, orgEntity, actions, new EffectConfig()));
		}

		[Fact]
		void country_card_playable_in_any_available_country_prevents_destroy() {
			var world = BuildWorld(out int orgEntity);
			AddCountry(world, "country-b");
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "needs-country-control");
			AddControl(world, "other-org", "country-b", 1);

			Assert.False(TryDestroy(
				world, orgEntity, CountryControlActionConfig(), new EffectConfig()));
		}

		[Fact]
		void destroyed_countries_are_not_country_card_playability_contexts() {
			var world = BuildWorld(out int orgEntity);
			int destroyedCountry = AddCountry(world, "country-b");
			world.Add(destroyedCountry, new IsDestroyed());
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "needs-country-control");
			AddControl(world, "other-org", "country-b", 1);

			Assert.True(TryDestroy(
				world, orgEntity, CountryControlActionConfig(), new EffectConfig()));
		}

		[Fact]
		void action_effect_classifier_detects_only_positive_control_changes() {
			var effects = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "positive", Amount = 1 },
					new ControlChangeEffectParams { EffectId = "negative", Amount = -1 }
				}
			};

			Assert.True(ActionEffectClassifier.RaisesControl(
				new ActionDefinition { EffectIds = new List<string> { "positive" } }, effects));
			Assert.False(ActionEffectClassifier.RaisesControl(
				new ActionDefinition { EffectIds = new List<string> { "negative" } }, effects));
		}

		[Fact]
		void converter_projects_destroy_fifo_and_player_flag_before_completion() {
			var world = BuildWorld(out int orgEntity);
			world.Add(orgEntity, new IsOrgDestroyed());
			int eventEntity = world.Create();
			world.Add(eventEntity, new OrgDestroyedApplied { OrganizationId = OrgId });
			int completionEntity = world.Create();
			world.Add(completionEntity, new GameCompletion {
				IsCompleted = true,
				WinnerOrganizationId = "other"
			});
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime());
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			var state = new VisualState();
			var order = new List<string>();
			state.OrgDestroyedResults.PropertyChanged += (_, __) => order.Add("destroy");
			state.GameCompletion.PropertyChanged += (_, __) => order.Add("completion");

			new VisualStateConverter(state, _resources, _relations).Update(
				0, world, gameTimeEntity, localeEntity, orgEntity);

			OrgDestroyedSnapshotState snapshot = Assert.Single(state.OrgDestroyedResults.Entries);
			Assert.Equal(OrgId, snapshot.OrganizationId);
			Assert.True(state.PlayerOrganization.IsDestroyed);
			Assert.Equal(new[] { "destroy", "completion" }, order);
			CleanupEffectNotificationsSystem.UpdateOrgDestroyed(world);
			Assert.Equal(0, Count<OrgDestroyedApplied>(world));
		}

		bool TryDestroy(World world, int orgEntity, ActionConfig actions, EffectConfig effects) {
			return OrgDestroySystem.TryDestroyIfConditionsMet(
				world, orgEntity, actions, effects, _resources, _relations,
				new GameSettings { DiscardGoldCost = 50 }, 100);
		}

		static World BuildWorld(out int orgEntity) {
			var world = new World();
			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = OrgId });
			world.Add(orgEntity, new OrganizationGameOutcome {
				ParticipationOrder = 0,
				Result = OrganizationGameResult.InProgress
			});
			AddCountry(world, "country-a");
			return world;
		}

		static int AddCountry(World world, string countryId) {
			int country = world.Create();
			world.Add(country, new Country { CountryId = countryId });
			return country;
		}

		static void AddDeck(World world, CardOwnerKind ownerKind, int handSize) {
			int entity = world.Create();
			world.Add(entity, new CardDeck { OrgId = OrgId });
			world.Add(entity, new CardOwnerType(ownerKind));
			world.Add(entity, new CardHand { HandSize = handSize });
		}

		static void AddCard(World world, CardOwnerKind ownerKind, string actionId) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = OrgId });
			world.Add(entity, new CardOwnerType(ownerKind));
			world.Add(entity, new CardInHand { SlotIndex = 0 });
		}

		static void AddGold(World world, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(OrgId, OwnerType.Org));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.Gold, Value = value });
		}

		static void AddControl(World world, int value) {
			AddControl(world, OrgId, "country-a", value);
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"test-control-{orgId}-{countryId}"
			});
		}

		static ActionConfig CountryControlActionConfig() {
			return new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "needs-country-control",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "totalCountryControl" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							}
						}
					}
				}
			};
		}

		static int Count<T>(IReadOnlyWorld world) where T : struct {
			int count = 0;
			int[] required = { TypeId<T>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				count += archetype.Count;
			}
			return count;
		}
	}
}
