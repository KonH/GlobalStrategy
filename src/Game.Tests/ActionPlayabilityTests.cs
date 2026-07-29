using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ActionPlayabilityTests {
		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "org_card",
						OwnerType = "org",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					},
					new ActionDefinition {
						ActionId = "country_card",
						OwnerType = "country",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "control" },
									new ExpressionNode { Type = "value", Value = 10 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 20.0 } }
					},
					new ActionDefinition {
						ActionId = "make_friend",
						OwnerType = "country",
						TargetRole = "diplomacy_advisor",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 30 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "hasSuitableRelationTarget" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					},
					new ActionDefinition {
						ActionId = "stop_friendship",
						OwnerType = "country",
						TargetRole = "diplomacy_advisor",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 80 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "relationStillExists" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 100.0 } }
					},
					new ActionDefinition {
						ActionId = "decrease_enemy_control",
						OwnerType = "country",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "hasEnemyControl" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							}
						},
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 250.0 } }
					}
				}
			};
		}

		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static int AddDiplomacyAdvisor(World world, string countryId, string charId, string orgId, int opinion) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = charId, CountryId = countryId, OrgId = "", RoleId = "diplomacy_advisor",
				NamePartKeys = System.Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner(charId, OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = $"opinion_{orgId}", Value = opinion });
			return charEntity;
		}

		static int AddGold(World world, string orgId, double amount) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(orgId));
			world.Add(e, new Resource { ResourceId = "gold", Value = amount });
			return e;
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int e = world.Create();
			world.Add(e, new ControlEffect { OrgId = orgId, CountryId = countryId, Value = value, EffectId = "test_control" });
		}

		static int AddCard(World world, string orgId, string actionId, string? countryId) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			if (countryId != null) {
				world.Add(e, new CountryContext { CountryId = countryId });
			}
			world.Add(e, new CardInHand { SlotIndex = 0 });
			world.Add(e, new CardUse());
			return e;
		}

		static int AddRelationCard(World world, string orgId, string actionId, string countryId, string targetCountryId, RelationKind kind) {
			int e = AddCard(world, orgId, actionId, countryId);
			world.Add(e, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = kind });
			return e;
		}

		static bool? RunPipeline(World world, ActionConfig config, int entity) {
			CheckActionConditionSystem.Update(world, config);
			DeductActionCostSystem.Update(world, config);
			ActionSucceededSystem.Update(world, config);
			if (world.Has<ActionSucceeded>(entity)) { return true; }
			if (world.Has<ActionFailed>(entity)) { return false; }
			return null;
		}

		[Fact]
		void evaluate_verdict_matches_pipeline_action_valid_outcome() {
			var config = BuildActionConfig();

			// org card, no country, affordable -> playable.
			var worldA = new World();
			AddGold(worldA, "OrgA", 100.0);
			int cardA = AddCard(worldA, "OrgA", "org_card", null);
			bool expectedA = ActionPlayability.Evaluate(worldA, config, -1, "org_card", "OrgA", null);
			Assert.Equal(expectedA, RunPipeline(worldA, config, cardA));
			Assert.True(expectedA);

			// org card, unaffordable -> unplayable.
			var worldB = new World();
			AddGold(worldB, "OrgA", 10.0);
			int cardB = AddCard(worldB, "OrgA", "org_card", null);
			bool expectedB = ActionPlayability.Evaluate(worldB, config, -1, "org_card", "OrgA", null);
			Assert.Equal(expectedB, RunPipeline(worldB, config, cardB));
			Assert.False(expectedB);

			// country card, control-threshold condition met and affordable -> playable.
			var worldC = new World();
			AddGold(worldC, "OrgA", 100.0);
			AddControl(worldC, "OrgA", "Prussia", 10);
			int cardC = AddCard(worldC, "OrgA", "country_card", "Prussia");
			bool expectedC = ActionPlayability.Evaluate(worldC, config, -1, "country_card", "OrgA", "Prussia");
			Assert.Equal(expectedC, RunPipeline(worldC, config, cardC));
			Assert.True(expectedC);

			// country card, control-threshold condition unmet -> unplayable.
			var worldD = new World();
			AddGold(worldD, "OrgA", 100.0);
			AddControl(worldD, "OrgA", "Prussia", 5);
			int cardD = AddCard(worldD, "OrgA", "country_card", "Prussia");
			bool expectedD = ActionPlayability.Evaluate(worldD, config, -1, "country_card", "OrgA", "Prussia");
			Assert.Equal(expectedD, RunPipeline(worldD, config, cardD));
			Assert.False(expectedD);

			// unknown actionId -> false; cannot be represented as a played card at all, direct call only.
			Assert.False(ActionPlayability.Evaluate(worldD, config, -1, "does_not_exist", "OrgA", null));
		}

		[Fact]
		void unaffordable_play_still_discards_card_and_deducts_nothing() {
			var config = BuildActionConfig();
			var world = new World();
			int goldEntity = AddGold(world, "OrgA", 5.0);
			AddControl(world, "OrgA", "Prussia", 10);
			int card = AddCard(world, "OrgA", "country_card", "Prussia");

			bool expected = ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia");
			Assert.False(expected);

			CheckActionConditionSystem.Update(world, config);
			DeductActionCostSystem.Update(world, config);
			ActionSucceededSystem.Update(world, config);
			RemoveCardFromHandSystem.Update(world);

			Assert.True(world.Has<ActionFailed>(card));
			Assert.False(world.Has<ActionSucceeded>(card));
			Assert.Equal(5.0, world.Get<Resource>(goldEntity).Value);
			Assert.False(world.Has<CardInHand>(card));
			Assert.True(world.Has<CardDiscard>(card));
		}

		[Fact]
		void deduct_uses_same_resource_entity_lookup_as_affordability() {
			var config = BuildActionConfig();
			var world = new World();
			int goldA = AddGold(world, "OrgA", 100.0);
			int goldB = AddGold(world, "OrgB", 100.0);
			AddCard(world, "OrgA", "org_card", null);

			CheckActionConditionSystem.Update(world, config);
			DeductActionCostSystem.Update(world, config);

			int found = ActionPlayability.FindResourceEntity(world, "OrgA", "gold");
			Assert.Equal(goldA, found);
			Assert.Equal(50.0, world.Get<Resource>(goldA).Value);
			Assert.Equal(100.0, world.Get<Resource>(goldB).Value);
		}

		[Fact]
		void make_friend_unplayable_when_opinion_below_threshold_even_with_suitable_target() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 29);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia"));
		}

		[Fact]
		void make_friend_unplayable_when_no_suitable_target_even_with_high_opinion() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia"));
		}

		[Fact]
		void make_friend_playable_when_opinion_at_threshold_and_suitable_target_exists() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 30);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia"));
		}

		[Fact]
		void make_friend_unaffordable_despite_gates_satisfied_is_unplayable() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 10.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia"));
		}

		[Fact]
		void existing_control_gated_card_unaffected_by_opinion_wiring() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);

			// No diplomacy advisor/opinion/relation data seeded at all — control-only card must
			// still evaluate purely off Control, unaffected by the new Opinion/HasSuitableRelationTarget wiring.
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia"));
		}

		[Fact]
		void stop_friendship_unplayable_when_named_relation_no_longer_holds() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// No Friend relation ever set between Prussia and Austria — the named relation is dead.
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia"));
		}

		[Fact]
		void stop_friendship_playable_when_named_relation_still_holds_opinion_and_cost_met() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.True(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia"));
		}

		[Fact]
		void stop_friendship_unplayable_when_opinion_below_threshold_even_if_relation_still_holds() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 79);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia"));
		}

		[Fact]
		void stop_friendship_unplayable_when_unaffordable_even_if_gates_satisfied() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 99.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia"));
		}

		[Fact]
		void stop_friendship_dead_instance_stays_unplayable_even_when_a_different_relation_of_same_kind_exists() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddCountry(world, "Bavaria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// A Friend relation with a *different* country exists, but this instance names Austria specifically.
			CountryRelations.SetRelation(world, "Prussia", "Bavaria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia"));
		}

		[Fact]
		void entity_negative_one_with_relation_agnostic_conditions_does_not_throw() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);

			// Regression guard: entity == -1 must never attempt World.Has<RelationCardTarget>(-1),
			// which would throw IndexOutOfRangeException without the entity >= 0 guard.
			var exception = Record.Exception(() => ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia"));
			Assert.Null(exception);
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia"));
		}

		[Fact]
		void decrease_enemy_control_unplayable_when_no_other_org_holds_control() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 250.0);
			AddControl(world, "OrgA", "Prussia", 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia"));
		}

		[Fact]
		void decrease_enemy_control_playable_when_another_org_holds_control_and_affordable() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 250.0);
			AddControl(world, "OrgB", "Prussia", 10);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia"));
		}

		[Fact]
		void decrease_enemy_control_unplayable_when_unaffordable_even_with_enemy_control_present() {
			var config = BuildActionConfig();
			var world = new World();
			AddGold(world, "OrgA", 249.0);
			AddControl(world, "OrgB", "Prussia", 10);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia"));
		}
	}
}
