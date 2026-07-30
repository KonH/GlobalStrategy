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
						ActionId = "ultimatum",
						OwnerType = "country",
						TargetRole = "military_advisor",
						Conditions = new List<ExpressionNode> {
							Gte("control", 10),
							Gte("opinion", 50),
							Gte("isInWar", 1),
							Gte("warProgress", 50)
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

		static void SetWarProgress(World world, double value) {
			int[] required = { TypeId<WarProgress>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var progresses = arch.GetColumn<WarProgress>();
				for (int i = 0; i < arch.Count; i++) {
					progresses[i].Value = value;
				}
			}
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
		void draw_excludes_ultimatum_when_not_in_any_war() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "ultimatum");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.False(IsInHand(world, card));
		}

		[Fact]
		void draw_includes_ultimatum_when_all_gates_satisfied() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, 50);
			int card = AddDeckCard(world, "OrgA", "Prussia", "ultimatum");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, card));
		}

		[Fact]
		void draw_resolves_opinion_per_candidate_role_not_once_for_the_whole_deck_pass() {
			var config = BuildActionConfig();
			var world = new World();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddControl(world, "OrgA", "Prussia", 10);
			// Diplomacy advisor's opinion satisfies stop_friendship; military advisor's does not satisfy ultimatum.
			AddDiplomacyAdvisor(world, "Prussia", "diplo1", "OrgA", opinion: 80);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 10);
			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			Wars.DeclareWar(world, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, 50);
			int stopFriendshipCard = AddRelationDeckCard(world, "OrgA", "Prussia", "stop_friendship", "Austria", RelationKind.Friend);
			int ultimatumCard = AddDeckCard(world, "OrgA", "Prussia", "ultimatum");

			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = "OrgA", CountryId = "Prussia" });
			world.Add(deckEntity, new CardDraw { Count = 2 });
			DrawCardSystem.Update(world, config, new Random(1));

			Assert.True(IsInHand(world, stopFriendshipCard));
			Assert.False(IsInHand(world, ultimatumCard));
		}
	}
}
