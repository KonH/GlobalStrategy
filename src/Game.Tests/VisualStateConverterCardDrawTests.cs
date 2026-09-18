using System.Collections.Generic;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterCardDrawTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static ActionConfig BuildActionConfig() {
			return TestActionConfig.Create()
				.HandSize("country", 3)
				.Action("hand_card", chance: 1)
				.Action("offered_card", chance: 1, cost: new[] {
					new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 10 }
				})
				.Action("offered_card_two", chance: 1)
				.Action("deck_card", chance: 1)
				.Build();
		}

		/// <summary>Selected Prussia plus an eight-card country deck; the deck entity is <c>"deck"</c>.</summary>
		static TestWorld BuildWorld() {
			return TestWorld.CreateWithSelectedCountry()
				.Deck(handSize: 8)
				.As("deck");
		}

		/// <summary>A deck card (no <see cref="CardInHand"/>) targeted at <paramref name="primaryCountryId"/>.</summary>
		static int AddCard(TestWorld world, string actionId, string primaryCountryId) {
			return world.Card(actionId, slotIndex: null, countryId: primaryCountryId).Last;
		}

		VisualStateProbe Probe(CountryConfig? countryConfig = null) {
			return new VisualStateProbe(BuildActionConfig(), _resources, _relations, countryConfig: countryConfig);
		}

		[Fact]
		void pending_offer_is_projected_in_choice_order_and_excluded_from_deck() {
			TestWorld world = BuildWorld();
			world.Add(world.Id("deck"), new PendingCardDraw { OptionCount = 2 });
			int handCard = AddCard(world, "hand_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });
			int secondOfferedCard = AddCard(world, "offered_card_two", "Prussia");
			world.Add(secondOfferedCard, new CardDrawChoice { ChoiceIndex = 1 });
			int offeredCard = AddCard(world, "offered_card", "Austria");
			world.Add(offeredCard, new RelationCardTarget { TargetCountryId = "France", Kind = RelationKind.Friend });
			world.Add(offeredCard, new CardDrawChoice { ChoiceIndex = 0 });
			AddCard(world, "deck_card", "Prussia");
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", IsAvailable = true }
				}
			};

			VisualStateProbe probe = Probe(countryConfig).Update(world);

			CountryActionsState actions = probe.State.SelectedCountry.CountryActions;
			Assert.Equal(8, actions.HandSize);
			Assert.True(actions.HasPendingDraw);
			Assert.False(actions.CanStartDraw);
			ActionCardEntry projectedHandCard = Assert.Single(actions.Hand);
			Assert.Equal("hand_card", projectedHandCard.ActionId);
			Assert.Equal("Prussia", projectedHandCard.CountryContextId);
			Assert.Collection(
				actions.DrawChoices,
				choice => {
					Assert.Equal(0, choice.ChoiceIndex);
					Assert.Equal("offered_card", choice.Card.ActionId);
					Assert.Equal("Austria", choice.Card.CountryContextId);
					Assert.Equal("France", choice.Card.TargetCountryId);
					Assert.True(choice.Card.IsUnplayable);
					Assert.Equal(new[] { "Austria", "Prussia" }, choice.Card.PlayableCountryIds);
				},
				choice => {
					Assert.Equal(1, choice.ChoiceIndex);
					Assert.Equal("offered_card_two", choice.Card.ActionId);
				});
			Assert.DoesNotContain(actions.Deck, entry => entry.ActionId == "offered_card");
			Assert.DoesNotContain(actions.Deck, entry => entry.ActionId == "offered_card_two");
			Assert.Contains(actions.Deck, entry => entry.ActionId == "deck_card");
		}

		[Fact]
		void available_draw_uses_authoritative_deck_cap_instead_of_config_default() {
			TestWorld world = BuildWorld();
			AddCard(world, "deck_card", "Prussia");

			VisualStateProbe probe = Probe().Update(world);

			CountryActionsState actions = probe.State.SelectedCountry.CountryActions;
			Assert.Equal(8, actions.HandSize);
			Assert.False(actions.HasPendingDraw);
			Assert.True(actions.CanStartDraw);
			Assert.Empty(actions.DrawChoices);
		}

		[Fact]
		void malformed_offer_markers_are_not_projected_as_pending_choices() {
			TestWorld world = BuildWorld();
			world.Add(world.Id("deck"), new PendingCardDraw { OptionCount = 2 });
			int orphanChoice = AddCard(world, "offered_card", "Prussia");
			world.Add(orphanChoice, new CardDrawChoice { ChoiceIndex = 0 });
			AddCard(world, "deck_card", "Prussia");

			VisualStateProbe probe = Probe().Update(world);

			CountryActionsState actions = probe.State.SelectedCountry.CountryActions;
			Assert.False(actions.HasPendingDraw);
			Assert.False(actions.CanStartDraw);
			Assert.Empty(actions.DrawChoices);
		}
	}
}
