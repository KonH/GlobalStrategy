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
					},
					new ActionDefinition {
						ActionId = "sell_arms",
						OwnerType = "country",
						TargetRole = "military_advisor",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "isInWar" },
									new ExpressionNode { Type = "value", Value = 1 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 80 }
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

		static void AddAdvisor(World world, string countryId, string charId, string orgId, string roleId, int opinion) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = charId, CountryId = countryId, OrgId = "", RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner(charId, OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = $"opinion_{orgId}", Value = opinion });
		}

		static void AddDiplomacyAdvisor(World world, string countryId, string charId, string orgId, int opinion) {
			AddAdvisor(world, countryId, charId, orgId, "diplomacy_advisor", opinion);
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
		void draw_skips_sell_arms_without_war_or_sufficient_military_opinion() {
			var config = BuildActionConfig();

			var peacefulWorld = new World();
			AddAdvisor(peacefulWorld, "Prussia", "general", "OrgA", "military_advisor", 80);
			int peacefulCard = AddDeckCard(peacefulWorld, "OrgA", "Prussia", "sell_arms");
			int peacefulDeck = peacefulWorld.Create();
			peacefulWorld.Add(peacefulDeck, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			peacefulWorld.Add(peacefulDeck, new CardDraw { Count = 1 });
			DrawCardSystem.Update(peacefulWorld, config, new Random(1));

			var lowOpinionWorld = new World();
			AddAdvisor(lowOpinionWorld, "Prussia", "diplomat", "OrgA", "diplomacy_advisor", 100);
			AddAdvisor(lowOpinionWorld, "Prussia", "general", "OrgA", "military_advisor", 79);
			Wars.DeclareWar(lowOpinionWorld, "Prussia", "Austria", new DateTime(1880, 1, 1));
			int lowOpinionCard = AddDeckCard(lowOpinionWorld, "OrgA", "Prussia", "sell_arms");
			int lowOpinionDeck = lowOpinionWorld.Create();
			lowOpinionWorld.Add(lowOpinionDeck, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			lowOpinionWorld.Add(lowOpinionDeck, new CardDraw { Count = 1 });
			DrawCardSystem.Update(lowOpinionWorld, config, new Random(1));

			Assert.False(IsInHand(peacefulWorld, peacefulCard));
			Assert.False(IsInHand(lowOpinionWorld, lowOpinionCard));
		}

		[Fact]
		void sell_arms_becomes_eligible_on_later_requested_draw() {
			var config = BuildActionConfig();
			var world = new World();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 80);
			int card = AddDeckCard(world, "OrgA", "Prussia", "sell_arms");
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deck, new CardDraw { Count = 1 });

			DrawCardSystem.Update(world, config, new Random(1));
			Assert.False(IsInHand(world, card));

			Wars.DeclareWar(world, "Prussia", "Austria", new DateTime(1880, 1, 1));
			Assert.False(IsInHand(world, card));

			world.Add(deck, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}
	}
}
