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
	// Covers DebugOrgCardAvailabilityProjector - the debug menu's "My org" / "Selected org"
	// deck+hand listings. Unlike CountryActions.Deck/Hand, these always reflect the target org's
	// full card availability (org cards merged with country cards) regardless of whether a
	// country is selected, and gate per-card detail on explicit deckDetailOpen/handDetailOpen
	// flags the caller passes in (formerly the DebugOrgCardVisibility flags read every tick).
	public class DebugOrgCardAvailabilityProjectorTests {
		const string PlayerOrgId = "OrgA";
		const string ForeignOrgId = "OrgB";
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 1);
		static readonly Dictionary<string, string> HqCountryByOrgId = new();

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
						Chance = 1,
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
					new ActionDefinition { ActionId = "org_card", OwnerType = "org", Chance = 1 }
				}
			};
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
			var world = new World();
			int countryCard = AddCountryCard(world, PlayerOrgId, "control_gated_card", "Prussia");
			world.Add(countryCard, new CardInHand { SlotIndex = 0 });
			AddOrgCard(world, PlayerOrgId, "org_card");

			var target = new OrgCardAvailabilityState();
			DebugOrgCardAvailabilityProjector.Project(
				world, target, PlayerOrgId, CurrentTime,
				deckDetailOpen: false, handDetailOpen: false,
				BuildActionConfig(), null, _resources, _relations, HqCountryByOrgId, 100);

			Assert.True(target.IsValid);
			Assert.Equal(PlayerOrgId, target.OrgId);
			Assert.Single(target.Hand);
			Assert.Single(target.Deck);
			Assert.Equal("control_gated_card", target.Hand[0].ActionId);
			Assert.Equal("org_card", target.Deck[0].ActionId);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		void my_org_hand_detail_is_gated_by_the_explicit_flag(bool handOpen) {
			var world = new World();
			int countryCard = AddCountryCard(world, PlayerOrgId, "control_gated_card", "Prussia");
			world.Add(countryCard, new CardInHand { SlotIndex = 0 });

			var target = new OrgCardAvailabilityState();
			DebugOrgCardAvailabilityProjector.Project(
				world, target, PlayerOrgId, CurrentTime,
				deckDetailOpen: false, handDetailOpen: handOpen,
				BuildActionConfig(), null, _resources, _relations, HqCountryByOrgId, 100);

			ActionCardEntry hand = Assert.Single(target.Hand);
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
		void resolve_selected_org_id_reflects_org_lens_dominant_org() {
			string resolved = DebugOrgCardAvailabilityProjector.ResolveSelectedOrgId(MapLens.Org, true, ForeignOrgId);
			Assert.Equal(ForeignOrgId, resolved);
		}

		[Fact]
		void resolve_selected_org_id_is_empty_outside_org_lens() {
			string resolved = DebugOrgCardAvailabilityProjector.ResolveSelectedOrgId(MapLens.Political, true, ForeignOrgId);
			Assert.Equal("", resolved);
		}

		[Fact]
		void resolve_selected_org_id_is_empty_when_org_lens_resources_invalid() {
			string resolved = DebugOrgCardAvailabilityProjector.ResolveSelectedOrgId(MapLens.Org, false, ForeignOrgId);
			Assert.Equal("", resolved);
		}

		[Fact]
		void selected_org_card_availability_reflects_resolved_org() {
			var world = new World();
			int foreignCard = AddCountryCard(world, ForeignOrgId, "control_gated_card", "Prussia");
			world.Add(foreignCard, new CardInHand { SlotIndex = 0 });
			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = ForeignOrgId, CountryId = "Prussia", Value = 20, EffectId = "test_control" });

			string selectedOrgId = DebugOrgCardAvailabilityProjector.ResolveSelectedOrgId(MapLens.Org, true, ForeignOrgId);
			var target = new OrgCardAvailabilityState();
			DebugOrgCardAvailabilityProjector.Project(
				world, target, selectedOrgId, CurrentTime,
				deckDetailOpen: false, handDetailOpen: false,
				BuildActionConfig(), null, _resources, _relations, HqCountryByOrgId, 100);

			Assert.True(target.IsValid);
			Assert.Equal(ForeignOrgId, target.OrgId);
			Assert.Single(target.Hand);
			Assert.Equal("control_gated_card", target.Hand[0].ActionId);
		}

		[Fact]
		void empty_org_id_projects_invalid_state() {
			var world = new World();
			var target = new OrgCardAvailabilityState();
			DebugOrgCardAvailabilityProjector.Project(
				world, target, "", CurrentTime,
				deckDetailOpen: false, handDetailOpen: false,
				BuildActionConfig(), null, _resources, _relations, HqCountryByOrgId, 100);

			Assert.False(target.IsValid);
			Assert.Empty(target.Hand);
			Assert.Empty(target.Deck);
		}
	}
}
