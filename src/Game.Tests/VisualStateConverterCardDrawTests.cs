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
	public class VisualStateConverterCardDrawTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "hand_card", OwnerType = "country", Chance = 1 },
					new ActionDefinition {
						ActionId = "offered_card",
						OwnerType = "country",
						Chance = 1,
						Cost = new List<ActionCost> {
							new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 10 }
						}
					},
					new ActionDefinition { ActionId = "offered_card_two", OwnerType = "country", Chance = 1 },
					new ActionDefinition { ActionId = "deck_card", OwnerType = "country", Chance = 1 }
				}
			};
		}

		static World BuildWorld(
			out int gameTimeEntity,
			out int localeEntity,
			out int orgEntity,
			out int deckEntity) {
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

			deckEntity = world.Create();
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
		void pending_offer_is_projected_in_choice_order_and_excluded_from_deck() {
			World world = BuildWorld(
				out int gameTimeEntity, out int localeEntity, out int orgEntity, out int deckEntity);
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 2 });
			int handCard = AddCard(world, "hand_card", "Prussia");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });
			int secondOfferedCard = AddCard(world, "offered_card_two", "Prussia");
			world.Add(secondOfferedCard, new CardDrawChoice { ChoiceIndex = 1 });
			int offeredCard = AddCard(world, "offered_card", "Austria");
			world.Add(offeredCard, new RelationCardTarget { TargetCountryId = "France", Kind = RelationKind.Friend });
			world.Add(offeredCard, new CardDrawChoice { ChoiceIndex = 0 });
			AddCard(world, "deck_card", "Prussia");

			var state = new VisualState();
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", IsAvailable = true }
				}
			};
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), countryConfig: countryConfig);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			CountryActionsState actions = state.SelectedCountry.CountryActions;
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
			World world = BuildWorld(
				out int gameTimeEntity, out int localeEntity, out int orgEntity, out _);
			AddCard(world, "deck_card", "Prussia");

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			CountryActionsState actions = state.SelectedCountry.CountryActions;
			Assert.Equal(8, actions.HandSize);
			Assert.False(actions.HasPendingDraw);
			Assert.True(actions.CanStartDraw);
			Assert.Empty(actions.DrawChoices);
		}

		[Fact]
		void malformed_offer_markers_are_not_projected_as_pending_choices() {
			World world = BuildWorld(
				out int gameTimeEntity, out int localeEntity, out int orgEntity, out int deckEntity);
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 2 });
			int orphanChoice = AddCard(world, "offered_card", "Prussia");
			world.Add(orphanChoice, new CardDrawChoice { ChoiceIndex = 0 });
			AddCard(world, "deck_card", "Prussia");

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			CountryActionsState actions = state.SelectedCountry.CountryActions;
			Assert.False(actions.HasPendingDraw);
			Assert.False(actions.CanStartDraw);
			Assert.Empty(actions.DrawChoices);
		}
	}
}
