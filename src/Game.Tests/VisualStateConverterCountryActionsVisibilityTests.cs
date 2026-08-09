using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
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
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "control_gated_card",
						OwnerType = "country",
						DeckCopies = 1,
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "control" },
									new ExpressionNode { Type = "value", Value = 10 }
								}
							}
						}
					}
				}
			};
		}

		static World BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity) {
			var world = new World();
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());

			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 8 });
			return world;
		}

		static int AddCard(World world, string actionId, string primaryCountryId) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = "OrgA" });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(entity, new CountryContext { CountryId = primaryCountryId });
			return entity;
		}

		[Fact]
		void hand_detail_is_skipped_when_actions_panel_closed() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int handCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });

			var state = new VisualState();
			var visibility = new CountryActionsVisibility { ActionsPanelOpen = false };
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), actionsVisibility: visibility);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry hand = Assert.Single(state.SelectedCountry.CountryActions.Hand);
			// Real condition (control >= 10, actual control is 0) would fail - the cheap
			// placeholder used while the panel is closed skips ActionPlayability entirely and
			// always reports playable/no conditions instead.
			Assert.True(hand.CanPlay);
			Assert.Empty(hand.Conditions);
			Assert.Empty(hand.PlayableCountryIds);
		}

		[Fact]
		void hand_detail_is_full_when_actions_panel_open() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int handCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });

			var state = new VisualState();
			var visibility = new CountryActionsVisibility { ActionsPanelOpen = true };
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), actionsVisibility: visibility);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry hand = Assert.Single(state.SelectedCountry.CountryActions.Hand);
			Assert.False(hand.CanPlay);
			Assert.NotEmpty(hand.Conditions);
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		void deck_entries_never_pay_for_full_evaluation(bool actionsPanelOpen) {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			AddCard(world, "control_gated_card", "Prussia");

			var state = new VisualState();
			var visibility = new CountryActionsVisibility { ActionsPanelOpen = actionsPanelOpen };
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), actionsVisibility: visibility);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry deck = Assert.Single(state.SelectedCountry.CountryActions.Deck);
			Assert.Equal("control_gated_card", deck.ActionId);
			// Deck cards are only ever rendered as a pile/count - real playability (which would
			// be false here, control 0 < 10) is never computed for them.
			Assert.True(deck.CanPlay);
			Assert.Empty(deck.Conditions);
		}

		[Fact]
		void draw_choices_keep_full_detail_even_when_panel_closed() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int deckEntity = -1;
			int[] deckReq = { TypeId<CardDeck>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				if (arch.Count > 0) { deckEntity = arch.Entities[0]; }
			}
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 1 });
			int offeredCard = AddCard(world, "control_gated_card", "Prussia");
			world.Add(offeredCard, new CardDrawChoice { ChoiceIndex = 0 });

			var state = new VisualState();
			var visibility = new CountryActionsVisibility { ActionsPanelOpen = false };
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), actionsVisibility: visibility);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			CardDrawChoiceEntry choice = Assert.Single(state.SelectedCountry.CountryActions.DrawChoices);
			Assert.False(choice.Card.CanPlay);
			Assert.NotEmpty(choice.Card.Conditions);
		}
	}
}
