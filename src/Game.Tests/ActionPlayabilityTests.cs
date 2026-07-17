using System.Collections.Generic;
using ECS;
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
					}
				}
			};
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
			bool expectedA = ActionPlayability.Evaluate(worldA, config, "org_card", "OrgA", null);
			Assert.Equal(expectedA, RunPipeline(worldA, config, cardA));
			Assert.True(expectedA);

			// org card, unaffordable -> unplayable.
			var worldB = new World();
			AddGold(worldB, "OrgA", 10.0);
			int cardB = AddCard(worldB, "OrgA", "org_card", null);
			bool expectedB = ActionPlayability.Evaluate(worldB, config, "org_card", "OrgA", null);
			Assert.Equal(expectedB, RunPipeline(worldB, config, cardB));
			Assert.False(expectedB);

			// country card, control-threshold condition met and affordable -> playable.
			var worldC = new World();
			AddGold(worldC, "OrgA", 100.0);
			AddControl(worldC, "OrgA", "Prussia", 10);
			int cardC = AddCard(worldC, "OrgA", "country_card", "Prussia");
			bool expectedC = ActionPlayability.Evaluate(worldC, config, "country_card", "OrgA", "Prussia");
			Assert.Equal(expectedC, RunPipeline(worldC, config, cardC));
			Assert.True(expectedC);

			// country card, control-threshold condition unmet -> unplayable.
			var worldD = new World();
			AddGold(worldD, "OrgA", 100.0);
			AddControl(worldD, "OrgA", "Prussia", 5);
			int cardD = AddCard(worldD, "OrgA", "country_card", "Prussia");
			bool expectedD = ActionPlayability.Evaluate(worldD, config, "country_card", "OrgA", "Prussia");
			Assert.Equal(expectedD, RunPipeline(worldD, config, cardD));
			Assert.False(expectedD);

			// unknown actionId -> false; cannot be represented as a played card at all, direct call only.
			Assert.False(ActionPlayability.Evaluate(worldD, config, "does_not_exist", "OrgA", null));
		}

		[Fact]
		void unaffordable_play_still_discards_card_and_deducts_nothing() {
			var config = BuildActionConfig();
			var world = new World();
			int goldEntity = AddGold(world, "OrgA", 5.0);
			AddControl(world, "OrgA", "Prussia", 10);
			int card = AddCard(world, "OrgA", "country_card", "Prussia");

			bool expected = ActionPlayability.Evaluate(world, config, "country_card", "OrgA", "Prussia");
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
	}
}
