using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class DrawCardSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
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
									new ExpressionNode { Type = "hasCountryRelation", RelationKind = "none", DesiredRelationKind = "friend" },
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
									new ExpressionNode { Type = "hasCountryRelation", RelationKind = "friend" },
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
						ActionId = "force_war_win",
						OwnerType = "country",
						TargetRole = "military_advisor",
						Conditions = new List<ExpressionNode> {
							Gte("control", 10),
							Gte("opinion", 50),
							Gte("isInWar", 1),
							Gte("warProgress", 50)
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
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 80 }
								}
							}
						}
					},
					new ActionDefinition {
						ActionId = "declare_revenge_war",
						OwnerType = "country",
						TargetRole = "military_advisor",
						Conditions = new List<ExpressionNode> {
							Gte("control", 20),
							Gte("opinion", 25),
							Gte("warFree", 1)
						}
					}
				}
			};
		}

		static ExpressionNode Gte(string fieldType, double value) {
			return new ExpressionNode {
				Type = "gte",
				Members = new List<ExpressionNode> {
					new ExpressionNode { Type = fieldType },
					new ExpressionNode { Type = "value", Value = value }
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

		static void AddMilitaryAdvisor(World world, string countryId, string charId, string orgId, int opinion) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = charId, CountryId = countryId, OrgId = "", RoleId = "military_advisor",
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner(charId, OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = $"opinion_{orgId}", Value = opinion });
		}

		void SetWarProgress(World world, double value) {
			int[] required = { TypeId<War>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var wars = arch.GetColumn<War>();
				for (int i = 0; i < arch.Count; i++) {
					ResourceMutations.TrySetValue(_resources, world, wars[i].WarId, ResourceDefinitions.WarProgress, value, out _);
				}
			}
		}

		static void AddDiplomacyAdvisor(World world, string countryId, string charId, string orgId, int opinion) {
			AddAdvisor(world, countryId, charId, orgId, "diplomacy_advisor", opinion);
		}

		static int AddDeckCard(World world, string orgId, string countryId, string actionId) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CardOwnerType(CardOwnerKind.Country));
			return e;
		}

		static int AddRelationDeckCard(World world, string orgId, string countryId, string actionId, string targetCountryId, RelationKind kind) {
			int e = AddDeckCard(world, orgId, countryId, actionId);
			world.Add(e, new CountryContext { CountryId = countryId });
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

		static void DrawToHandSize(World world, ActionConfig config, Random rng) {
			int deckEntity = -1;
			string orgId = "";
			int handSize = 0;
			int[] required = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].Value != CardOwnerKind.Country) {
						continue;
					}
					Assert.Equal(-1, deckEntity);
					deckEntity = arch.Entities[i];
					orgId = decks[i].OrgId;
					handSize = hands[i].HandSize;
				}
			}
			Assert.True(deckEntity >= 0);

			while (CountryCardDrawQuery.CountHandCards(world, orgId) < handSize) {
				DrawCardSystem.Update(
					world,
					config,
					new EffectConfig(),
					rng,
					new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = orgId } }),
					Array.Empty<DiscardCardResult>(),
					new CountryRelations(),
					orgId);
				IReadOnlyList<CountryCardDrawChoiceInfo> choices = CountryCardDrawQuery.GetChoices(world, orgId);
				if (choices.Count == 0) {
					break;
				}
				ReceiveCardSystem.Update(
					world,
					new ReadCommands<ReceiveCardCommand>(new[] {
						new ReceiveCardCommand { OrgId = orgId, ChoiceIndex = choices[0].ChoiceIndex }
					}));
			}
		}

		[Fact]
		void draw_ignores_make_friend_opinion_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_make_friend_relation_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
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
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_expired_relation_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// No Friend relation ever set between Prussia and Austria — the named relation is dead.
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_stop_friendship_when_named_relation_still_holds() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_enemy_control_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_decrease_enemy_control_when_another_org_holds_control() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgB", "Prussia", 10);
			int card = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_war_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "force_war_win");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_sell_arms_in_peacetime_with_sufficient_military_opinion() {
			var config = BuildActionConfig();
			var world = new World();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 80);
			int card = AddDeckCard(world, "OrgA", "Prussia", "sell_arms");
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = "OrgA" });
			world.Add(deck, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deck, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_sell_arms_opinion_requirement() {
			var config = BuildActionConfig();

			var lowOpinionPeaceful = new World();
			AddAdvisor(lowOpinionPeaceful, "Prussia", "diplomat", "OrgA", "diplomacy_advisor", 100);
			AddAdvisor(lowOpinionPeaceful, "Prussia", "general", "OrgA", "military_advisor", 79);
			int peacefulCard = AddDeckCard(lowOpinionPeaceful, "OrgA", "Prussia", "sell_arms");
			int peacefulDeck = lowOpinionPeaceful.Create();
			lowOpinionPeaceful.Add(peacefulDeck, new CardDeck { OrgId = "OrgA" });
			lowOpinionPeaceful.Add(peacefulDeck, new CardOwnerType(CardOwnerKind.Country));
			lowOpinionPeaceful.Add(peacefulDeck, new CardHand { HandSize = 1 });
			DrawToHandSize(lowOpinionPeaceful, config, new Random(1));

			var lowOpinionWartime = new World();
			AddAdvisor(lowOpinionWartime, "Prussia", "diplomat", "OrgA", "diplomacy_advisor", 100);
			AddAdvisor(lowOpinionWartime, "Prussia", "general", "OrgA", "military_advisor", 79);
			Wars.DeclareWar(lowOpinionWartime, _resources, "Prussia", "Austria", new DateTime(1880, 1, 1));
			int wartimeCard = AddDeckCard(lowOpinionWartime, "OrgA", "Prussia", "sell_arms");
			int wartimeDeck = lowOpinionWartime.Create();
			lowOpinionWartime.Add(wartimeDeck, new CardDeck { OrgId = "OrgA" });
			lowOpinionWartime.Add(wartimeDeck, new CardOwnerType(CardOwnerKind.Country));
			lowOpinionWartime.Add(wartimeDeck, new CardHand { HandSize = 1 });
			DrawToHandSize(lowOpinionWartime, config, new Random(1));

			Assert.True(IsInHand(lowOpinionPeaceful, peacefulCard));
			Assert.True(IsInHand(lowOpinionWartime, wartimeCard));
		}

		[Fact]
		void sell_arms_draw_does_not_wait_for_requirements() {
			var config = BuildActionConfig();
			var world = new World();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 79);
			int card = AddDeckCard(world, "OrgA", "Prussia", "sell_arms");
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = "OrgA" });
			world.Add(deck, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deck, new CardHand { HandSize = 1 });

			DrawToHandSize(world, config, new Random(1));
			Assert.True(IsInHand(world, card));

			Assert.True(ResourceMutations.TrySetValue(_resources, world, "general", "opinion_OrgA", 80, out _));
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
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_ultimatum_when_all_gates_satisfied() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "force_war_win");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_resolves_opinion_per_candidate_role_not_once_for_the_whole_deck_pass() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddControl(world, "OrgA", "Prussia", 10);
			// The cards have opposite advisor states, but neither requirement filters this draw.
			AddDiplomacyAdvisor(world, "Prussia", "diplo1", "OrgA", opinion: 80);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 10);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, 50);
			int stopFriendshipCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);
			int ultimatumCard = AddDeckCard(world, "OrgA", "Prussia", "force_war_win");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 2 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, stopFriendshipCard));
			Assert.True(IsInHand(world, ultimatumCard));
		}

		[Fact]
		void draw_relation_card_weight_does_not_put_same_entity_in_hand_twice() {
			var config = BuildActionConfig();
			config.Find("stop_friendship")!.DeckCopies = 5;
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 3 });
			DrawToHandSize(world, config, new Random(1));

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
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			AddControl(world, "OrgB", "Prussia", 10);
			int relationCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);
			int staticCard = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");
			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });

			int wins = 0;
			const int trials = 40;
			for (int t = 0; t < trials; t++) {
				if (world.Has<CardInHand>(relationCard)) { world.Remove<CardInHand>(relationCard); }
				if (world.Has<CardInHand>(staticCard)) { world.Remove<CardInHand>(staticCard); }
				DrawToHandSize(world, config, new Random(t + 1));
				if (IsInHand(world, relationCard)) { wins++; }
			}

			Assert.True(wins >= 35, $"expected weighted relation card to win most draws, won {wins}/{trials}");
		}

		[Fact]
		void force_draw_puts_specific_country_card_in_hand_ignoring_gates() {
			var world = new World();
			AddCountry(world, "Prussia");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 0);
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");

			bool drawn = DrawCardSystem.ForceDrawCard(world, "OrgA", "Prussia", "make_friend", "");

			Assert.True(drawn);
			Assert.True(IsInHand(world, card));
			Assert.Equal(0, world.Get<CardInHand>(card).SlotIndex);
		}

		[Fact]
		void force_draw_matches_relation_target_country() {
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddCountry(world, "France");
			int austriaCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);
			int franceCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "France", RelationKind.Friend);

			bool drawn = DrawCardSystem.ForceDrawCard(world, "OrgA", "Prussia", "stop_friendship", "France");

			Assert.True(drawn);
			Assert.False(IsInHand(world, austriaCard));
			Assert.True(IsInHand(world, franceCard));
		}

		[Fact]
		void force_draw_puts_org_card_in_hand() {
			var world = new World();
			int card = world.Create();
			world.Add(card, new GameAction { ActionId = "org_action" });
			world.Add(card, new OrgContext { OrgId = "OrgA" });
			world.Add(card, new CardOwnerType(CardOwnerKind.Org));

			bool drawn = DrawCardSystem.ForceDrawCard(world, "OrgA", "", "org_action", "");

			Assert.True(drawn);
			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void force_draw_returns_false_when_card_already_in_hand() {
			var world = new World();
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");
			world.Add(card, new CardInHand { SlotIndex = 0 });

			bool drawn = DrawCardSystem.ForceDrawCard(world, "OrgA", "Prussia", "make_friend", "");

			Assert.False(drawn);
		}

		[Fact]
		void force_discard_removes_hand_card_and_marks_discard() {
			var world = new World();
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");
			world.Add(card, new CardInHand { SlotIndex = 1 });

			bool discarded = RemoveCardFromHandSystem.ForceDiscard(
				world, "OrgA", "Prussia", "make_friend", "", slotIndex: 1);

			Assert.True(discarded);
			Assert.False(IsInHand(world, card));
			Assert.True(world.Has<CardDiscard>(card));
		}

		[Fact]
		void force_discard_does_not_trigger_production_draw() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 0);
			AddControl(world, "OrgB", "Prussia", 10);
			int handCard = AddDeckCard(world, "OrgA", "Prussia", "make_friend");
			world.Add(handCard, new CardInHand { SlotIndex = 0 });
			int deckCard = AddDeckCard(world, "OrgA", "Prussia", "decrease_enemy_control");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });

			Assert.True(RemoveCardFromHandSystem.ForceDiscard(
				world, "OrgA", "Prussia", "make_friend", "", slotIndex: 0));
			CleanupCardDiscardSystem.Update(world);

			Assert.False(IsInHand(world, handCard));
			Assert.False(world.Has<CardDiscard>(handCard));
			Assert.False(IsInHand(world, deckCard));
		}

		[Fact]
		void explicit_draw_can_be_retried_after_discard_cleanup() {
			var config = BuildActionConfig();
			var world = new World();
			int card = AddDeckCard(world, "OrgA", "Prussia", "make_friend");
			world.Add(card, new CardInHand { SlotIndex = 0 });
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = "OrgA" });
			world.Add(deck, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deck, new CardHand { HandSize = 1 });

			Assert.True(RemoveCardFromHandSystem.ForceDiscard(
				world, "OrgA", "Prussia", "make_friend", "", slotIndex: 0));
			DrawToHandSize(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
			Assert.False(world.Has<PendingCardDraw>(deck));

			CleanupCardDiscardSystem.Update(world);
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
			Assert.False(world.Has<PendingCardDraw>(deck));
		}

		[Fact]
		void draw_ignores_revenge_control_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 19);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "declare_revenge_war");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_revenge_opinion_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 24);
			int card = AddDeckCard(world, "OrgA", "Prussia", "declare_revenge_war");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_ignores_revenge_war_requirement() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Great_Britain", "Austria", new DateTime(1880, 1, 1));
			int card = AddDeckCard(world, "OrgA", "Prussia", "declare_revenge_war");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_revenge_when_all_conditions_hold() {
			var config = BuildActionConfig();
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 25);
			int card = AddDeckCard(world, "OrgA", "Prussia", "declare_revenge_war");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 1 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void mixed_make_friend_and_revenge_candidates_resolve_opinion_against_their_own_target_role() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddAdvisor(world, "Prussia", "diplo1", "OrgA", "diplomacy_advisor", 50);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 10);
			AddControl(world, "OrgA", "Prussia", 20);
			int friendCard = AddDeckCard(world, "OrgA", "Prussia", "make_friend");
			int revengeCard = AddDeckCard(world, "OrgA", "Prussia", "declare_revenge_war");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA" });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = 2 });
			DrawToHandSize(world, config, new Random(1));

			Assert.True(IsInHand(world, friendCard));
			Assert.True(IsInHand(world, revengeCard));
		}
	}
}
