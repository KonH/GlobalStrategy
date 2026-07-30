using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class DrawCardSystemTests {
		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Actions = new List<ActionDefinition> {
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
						}
					},
					new ActionDefinition {
						ActionId = "stop_friendship",
						OwnerType = "country",
						TargetRole = "diplomacy_advisor",
						DeckCopies = 1,
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
						}
					},
					new ActionDefinition {
						ActionId = "decrease_enemy_control",
						OwnerType = "country",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gt",
								Members = new List<ExpressionNode> {
									new ExpressionNode {
										Type = "sub",
										Members = new List<ExpressionNode> {
											new ExpressionNode { Type = "totalCountryControl" },
											new ExpressionNode { Type = "control" }
										}
									},
									new ExpressionNode { Type = "value", Value = 0 }
								}
							}
						}
					}
				}
			};
		}

		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static void AddDiplomacyAdvisor(World world, string countryId, string charId, string orgId, int opinion) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = charId, CountryId = countryId, OrgId = "", RoleId = "diplomacy_advisor",
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner(charId, OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = $"opinion_{orgId}", Value = opinion });
		}

		static int AddDeckCard(World world, string orgId, string countryId, string actionId) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CountryContext { CountryId = countryId });
			return e;
		}

		static int AddRelationDeckCard(World world, string orgId, string countryId, string actionId, string targetCountryId, RelationKind kind) {
			int e = AddDeckCard(world, orgId, countryId, actionId);
			world.Add(e, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = kind });
			return e;
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"test_{orgId}_{countryId}"
			});
		}

		static bool IsInHand(World world, int entity) {
			return world.Has<CardInHand>(entity);
		}

		[Fact]
		void draw_skips_make_friend_when_opinion_below_threshold() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_skips_make_friend_when_no_suitable_relation_target() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_make_friend_when_both_gates_satisfied() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_excludes_stop_friendship_when_named_relation_no_longer_holds() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// No Friend relation ever set between Prussia and Austria — the named relation is dead.
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_stop_friendship_when_named_relation_still_holds() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_excludes_decrease_enemy_control_when_no_other_org_holds_control() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_decrease_enemy_control_when_another_org_holds_control() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgB", "Prussia", 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_skips_relation_card_when_deck_copies_is_zero() {
			var config = BuildActionConfig();
			config.Find("stop_friendship")!.DeckCopies = 0;
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_relation_card_weight_does_not_put_same_entity_in_hand_twice() {
			var config = BuildActionConfig();
			config.Find("stop_friendship")!.DeckCopies = 5;
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 3 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
			Assert.Equal(0, world.Get<CardInHand>(card).SlotIndex);
		}

		[Fact]
		void draw_relation_card_with_higher_weight_beats_single_copy_static_card() {
			var config = BuildActionConfig();
			config.Find("stop_friendship")!.DeckCopies = 100;
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			AddControl(world, "OrgB", "Prussia", 10);
			int relationCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);
			int staticCard = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int wins = 0;
			const int trials = 40;
			for (int t = 0; t < trials; t++) {
				if (world.Has<CardInHand>(relationCard)) { world.Remove<CardInHand>(relationCard); }
				if (world.Has<CardInHand>(staticCard)) { world.Remove<CardInHand>(staticCard); }
				int deckEntity = world.Create();
				world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
				world.Add(deckEntity, new CardDraw { Count = 1 });
				DrawCardSystem.Update(world, config, new Random(t + 1));
				if (IsInHand(world, relationCard)) { wins++; }
			}

			Assert.True(wins >= 35, $"expected weighted relation card to win most draws, won {wins}/{trials}");
		}
	}
}
