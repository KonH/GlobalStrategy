using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	// Covers CountryActionsVisibility - VisualStateConverter.UpdateCountryActions skips the
	// expensive per-card ActionPlayability evaluation for hand cards while the actions
	// sub-panel is reported closed, but never for deck cards (always cheap) or draw choices
	// (always full detail - CardDrawAnimator needs them regardless of panel state).
	public class VisualStateConverterCountryActionsVisibilityTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static ActionConfig BuildActionConfig() {
			return TestActionConfig.Create()
				.HandSize("country", 3)
				.Action("control_gated_card", chance: 1, conditions: new[] { Expr.Gte("control", 10) })
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

		VisualStateProbe Probe(bool actionsPanelOpen) {
			return new VisualStateProbe(
				BuildActionConfig(),
				_resources,
				_relations,
				actionsVisibility: new CountryActionsVisibility { ActionsPanelOpen = actionsPanelOpen });
		}

		[Fact]
		void hand_detail_is_skipped_when_actions_panel_closed() {
			TestWorld world = BuildWorld();
			int handCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });

			VisualStateProbe probe = Probe(actionsPanelOpen: false).Update(world);

			ActionCardEntry hand = Assert.Single(probe.CountryHand);
			// Real condition (control >= 10, actual control is 0) would fail - the cheap
			// placeholder used while the panel is closed skips ActionPlayability entirely and
			// always reports playable/no conditions instead.
			Assert.True(hand.CanPlay);
			Assert.Empty(hand.Conditions);
			Assert.Empty(hand.PlayableCountryIds);
		}

		[Fact]
		void hand_detail_is_full_when_actions_panel_open() {
			TestWorld world = BuildWorld();
			int handCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });

			VisualStateProbe probe = Probe(actionsPanelOpen: true).Update(world);

			ActionCardEntry hand = Assert.Single(probe.CountryHand);
			Assert.False(hand.CanPlay);
			Assert.NotEmpty(hand.Conditions);
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		void deck_entries_never_pay_for_full_evaluation(bool actionsPanelOpen) {
			TestWorld world = BuildWorld();
			AddCard(world, "control_gated_card", "Prussia");

			VisualStateProbe probe = Probe(actionsPanelOpen).Update(world);

			ActionCardEntry deck = Assert.Single(probe.State.SelectedCountry.CountryActions.Deck);
			Assert.Equal("control_gated_card", deck.ActionId);
			// Deck cards are only ever rendered as a pile/count - real playability (which would
			// be false here, control 0 < 10) is never computed for them.
			Assert.True(deck.CanPlay);
			Assert.Empty(deck.Conditions);
		}

		[Fact]
		void draw_choices_keep_full_detail_even_when_panel_closed() {
			TestWorld world = BuildWorld();
			world.Add(world.Id("deck"), new PendingCardDraw { OptionCount = 1 });
			int offeredCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(offeredCard, new CardDrawChoice { ChoiceIndex = 0 });

			VisualStateProbe probe = Probe(actionsPanelOpen: false).Update(world);

			CardDrawChoiceEntry choice = Assert.Single(probe.State.SelectedCountry.CountryActions.DrawChoices);
			Assert.False(choice.Card.CanPlay);
			Assert.NotEmpty(choice.Card.Conditions);
		}
	}
}
