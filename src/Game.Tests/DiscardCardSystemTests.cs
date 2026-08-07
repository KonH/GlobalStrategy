using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DiscardCardSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		const string OrgId = "OrgA";
		const string CountryId = "Prussia";
		const string ActionId = "improve_control";
		const double DiscardCost = 50;

		static int AddGold(World world, double amount) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(OrgId));
			world.Add(entity, new Resource { ResourceId = ResourceDefinitions.Gold, Value = amount });
			return entity;
		}

		static int AddCard(
			World world,
			CardOwnerKind ownerKind = CardOwnerKind.Country,
			int slotIndex = 1,
			string actionId = ActionId,
			string targetCountryId = "") {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = OrgId });
			world.Add(entity, new CardOwnerType(ownerKind));
			world.Add(entity, new CardInHand { SlotIndex = slotIndex });
			if (!string.IsNullOrEmpty(targetCountryId)) {
				world.Add(entity, new RelationCardTarget { TargetCountryId = targetCountryId });
			}
			return entity;
		}

		static DiscardCardCommand Command(
			int slotIndex = 1,
			string actionId = ActionId,
			string targetCountryId = "",
			string countryId = CountryId) {
			return new DiscardCardCommand {
				OrgId = OrgId,
				CountryId = countryId,
				ActionId = actionId,
				TargetCountryId = targetCountryId,
				SlotIndex = slotIndex
			};
		}

		IReadOnlyList<DiscardCardResult> Run(
			World world,
			DiscardCardCommand command,
			double cost = DiscardCost) {
			return DiscardCardSystem.Update(world, new ReadCommands<DiscardCardCommand>(new[] { command }), cost, _resources);
		}

		[Fact]
		void successful_discard_deducts_exact_cost_and_marks_card_for_discard() {
			var world = new World();
			int gold = AddGold(world, 125);
			int card = AddCard(world);

			IReadOnlyList<DiscardCardResult> results = Run(world, Command());

			Assert.Single(results);
			Assert.Equal(DiscardCardOutcome.Succeeded, results[0].Outcome);
			Assert.Equal(75, world.Get<Resource>(gold).Value);
			Assert.False(world.Has<CardInHand>(card));
			Assert.True(world.Has<CardDiscard>(card));
		}

		[Fact]
		void insufficient_gold_is_observable_and_leaves_card_and_gold_unchanged() {
			var world = new World();
			int gold = AddGold(world, 49);
			int card = AddCard(world);

			IReadOnlyList<DiscardCardResult> results = Run(world, Command());

			Assert.Equal(DiscardCardOutcome.InsufficientGold, Assert.Single(results).Outcome);
			Assert.Equal(49, world.Get<Resource>(gold).Value);
			Assert.True(world.Has<CardInHand>(card));
			Assert.False(world.Has<CardDiscard>(card));
		}

		[Theory]
		[InlineData(2, ActionId, "France")]
		[InlineData(1, "improve_opinion", "France")]
		[InlineData(1, ActionId, "Austria")]
		void stale_slot_action_or_target_is_rejected(int slotIndex, string actionId, string targetCountryId) {
			var world = new World();
			int gold = AddGold(world, 100);
			int card = AddCard(world, targetCountryId: "France");

			IReadOnlyList<DiscardCardResult> results = Run(
				world,
				Command(slotIndex, actionId, targetCountryId));

			Assert.Equal(DiscardCardOutcome.CardNotFound, Assert.Single(results).Outcome);
			Assert.Equal(100, world.Get<Resource>(gold).Value);
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void org_card_cannot_be_discarded_through_country_card_command() {
			var world = new World();
			int gold = AddGold(world, 100);
			int card = AddCard(world, CardOwnerKind.Org);

			IReadOnlyList<DiscardCardResult> results = Run(world, Command());

			Assert.Equal(DiscardCardOutcome.CardNotFound, Assert.Single(results).Outcome);
			Assert.Equal(100, world.Get<Resource>(gold).Value);
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void missing_selected_country_is_rejected_before_lookup() {
			var world = new World();
			AddGold(world, 100);
			int card = AddCard(world);

			IReadOnlyList<DiscardCardResult> results = Run(world, Command(countryId: ""));

			Assert.Equal(DiscardCardOutcome.InvalidCommand, Assert.Single(results).Outcome);
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void discard_pipeline_refills_same_slot_with_different_entity_before_cleanup() {
			var world = new World();
			AddGold(world, 100);
			int discarded = AddCard(world, slotIndex: 0);
			int replacement = world.Create();
			world.Add(replacement, new GameAction { ActionId = "improve_opinion" });
			world.Add(replacement, new OrgContext { OrgId = OrgId });
			world.Add(replacement, new CardOwnerType(CardOwnerKind.Country));

			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = OrgId });
			world.Add(deck, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deck, new CardHand { HandSize = 1 });
			var config = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = ActionId, OwnerType = "country", DeckCopies = 1 },
					new ActionDefinition { ActionId = "improve_opinion", OwnerType = "country", DeckCopies = 1 }
				}
			};

			Assert.Equal(DiscardCardOutcome.Succeeded, Assert.Single(Run(world, Command(slotIndex: 0))).Outcome);
			CheckHandSizeSystem.Update(world);
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(world.Has<CardInHand>(discarded));
			Assert.True(world.Has<CardDiscard>(discarded));
			Assert.True(world.Has<CardInHand>(replacement));
			Assert.Equal(0, world.Get<CardInHand>(replacement).SlotIndex);
			CleanupCardDiscardSystem.Update(world);
		}

		[Fact]
		void game_logic_orders_discard_before_hand_refill_and_cleanup() {
			GameLogicContext context = MultiOrgTestSupport.BuildContext(includeCountryCard: true, rngSeed: 7);
			ActionConfig actionConfig = context.Action.Load();
			actionConfig.Actions.Add(new ActionDefinition {
				ActionId = "discard_replacement_a",
				OwnerType = "country",
				DeckCopies = 1
			});
			actionConfig.Actions.Add(new ActionDefinition {
				ActionId = "discard_replacement_b",
				OwnerType = "country",
				DeckCopies = 1
			});
			var logic = new GameLogic(context);
			logic.Update(0);

			(int discardedEntity, string actionId, int slotIndex) = FindFirstCountryHandCard(
				logic.World,
				MultiOrgTestSupport.OrgA);
			double goldBefore = logic.Resources.GetValue(
				logic.World,
				MultiOrgTestSupport.OrgA,
				ResourceDefinitions.Gold);
			logic.Commands.Push(new DiscardCardCommand {
				OrgId = MultiOrgTestSupport.OrgA,
				CountryId = MultiOrgTestSupport.HqA,
				ActionId = actionId,
				SlotIndex = slotIndex
			});

			logic.Update(0);

			Assert.Equal(
				goldBefore - logic.GameSettings.DiscardGoldCost,
				logic.Resources.GetValue(logic.World, MultiOrgTestSupport.OrgA, ResourceDefinitions.Gold));
			Assert.False(logic.World.Has<CardInHand>(discardedEntity));
			Assert.False(logic.World.Has<CardDiscard>(discardedEntity));
			Assert.NotEqual(discardedEntity, FindCountryHandCardInSlot(
				logic.World,
				MultiOrgTestSupport.OrgA,
				slotIndex));
		}

		static (int Entity, string ActionId, int SlotIndex) FindFirstCountryHandCard(World world, string orgId) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId && owners[i].Value == CardOwnerKind.Country) {
						return (arch.Entities[i], actions[i].ActionId, hands[i].SlotIndex);
					}
				}
			}
			throw new InvalidOperationException($"No country hand card found for {orgId}.");
		}

		static int FindCountryHandCardInSlot(World world, string orgId, int slotIndex) {
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId
						&& owners[i].Value == CardOwnerKind.Country
						&& hands[i].SlotIndex == slotIndex) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}

		[Fact]
		void negative_discard_cost_fails_fast() {
			var world = new World();

			var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(world, Command(), -1));

			Assert.Contains("discard gold cost", exception.Message, StringComparison.OrdinalIgnoreCase);
		}
	}
}
