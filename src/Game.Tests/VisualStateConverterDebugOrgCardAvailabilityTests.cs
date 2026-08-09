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
	// Covers VisualStateConverter.UpdateDebugOrgCardAvailability - the debug menu's "My org" /
	// "Selected org" deck+hand listings. Unlike CountryActions.Deck/Hand, these always reflect
	// the target org's full card availability (org cards merged with country cards) regardless
	// of whether a country is selected, and gate per-card detail on DebugOrgCardVisibility
	// instead of the gameplay actions-panel flag.
	public class VisualStateConverterDebugOrgCardAvailabilityTests {
		const string PlayerOrgId = "OrgA";
		const string ForeignOrgId = "OrgB";
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 8 },
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 4 }
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
					},
					new ActionDefinition { ActionId = "org_card", OwnerType = "org", DeckCopies = 1 }
				}
			};
		}

		static World BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity) {
			var world = new World();
			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = PlayerOrgId, DisplayName = PlayerOrgId });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			return world;
		}

		static int AddCountryCard(World world, string orgId, string actionId, string primaryCountryId) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = orgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(entity, new CountryContext { CountryId = primaryCountryId });
			return entity;
		}

		static int AddOrgCard(World world, string orgId, string actionId) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = orgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Org));
			return entity;
		}

		[Fact]
		void my_org_card_availability_merges_org_and_country_cards_without_selected_country() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int countryCard = AddCountryCard(world, PlayerOrgId, "control_gated_card", "Prussia");
			world.Add(countryCard, new CardInHand { SlotIndex = 0 });
			AddOrgCard(world, PlayerOrgId, "org_card");
			// No IsSelected anywhere - SelectedCountry stays invalid.

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.False(state.SelectedCountry.IsValid);
			Assert.True(state.MyOrgCardAvailability.IsValid);
			Assert.Equal(PlayerOrgId, state.MyOrgCardAvailability.OrgId);
			Assert.Single(state.MyOrgCardAvailability.Hand);
			Assert.Single(state.MyOrgCardAvailability.Deck);
			Assert.Equal("control_gated_card", state.MyOrgCardAvailability.Hand[0].ActionId);
			Assert.Equal("org_card", state.MyOrgCardAvailability.Deck[0].ActionId);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		void my_org_hand_detail_is_gated_by_visibility_flag(bool handOpen) {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int countryCard = AddCountryCard(world, PlayerOrgId, "control_gated_card", "Prussia");
			world.Add(countryCard, new CardInHand { SlotIndex = 0 });

			var state = new VisualState();
			var visibility = new DebugOrgCardVisibility { MyOrgHandOpen = handOpen };
			var converter = new VisualStateConverter(
				state, _resources, _relations, BuildActionConfig(), debugOrgCardVisibility: visibility);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry hand = Assert.Single(state.MyOrgCardAvailability.Hand);
			if (handOpen) {
				// Real condition (control >= 10, actual control is 0) fails once evaluated.
				Assert.False(hand.CanPlay);
				Assert.NotEmpty(hand.Conditions);
			} else {
				Assert.True(hand.CanPlay);
				Assert.Empty(hand.Conditions);
			}
		}

		[Fact]
		void selected_org_card_availability_reflects_org_lens_dominant_org() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());
			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = ForeignOrgId, CountryId = "Prussia", Value = 20, EffectId = "test_control" });
			int foreignCard = AddCountryCard(world, ForeignOrgId, "control_gated_card", "Prussia");
			world.Add(foreignCard, new CardInHand { SlotIndex = 0 });

			var state = new VisualState();
			state.MapLens.Set(MapLens.Org);
			var converter = new VisualStateConverter(state, _resources, _relations, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.True(state.SelectedOrgCardAvailability.IsValid);
			Assert.Equal(ForeignOrgId, state.SelectedOrgCardAvailability.OrgId);
			Assert.Single(state.SelectedOrgCardAvailability.Hand);
			Assert.Equal("control_gated_card", state.SelectedOrgCardAvailability.Hand[0].ActionId);
		}

		[Fact]
		void selected_org_card_availability_is_empty_outside_org_lens() {
			World world = BuildWorld(out int gameTimeEntity, out int localeEntity, out int orgEntity);
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());
			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = ForeignOrgId, CountryId = "Prussia", Value = 20, EffectId = "test_control" });
			AddCountryCard(world, ForeignOrgId, "control_gated_card", "Prussia");

			var state = new VisualState();
			// MapLens stays Political (default) - "Selected org" only resolves under the org lens.
			var converter = new VisualStateConverter(state, _resources, _relations, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.False(state.SelectedOrgCardAvailability.IsValid);
			Assert.Empty(state.SelectedOrgCardAvailability.Hand);
			Assert.Empty(state.SelectedOrgCardAvailability.Deck);
		}
	}
}
