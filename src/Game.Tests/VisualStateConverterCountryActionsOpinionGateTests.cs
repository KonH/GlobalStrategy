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
	public class VisualStateConverterCountryActionsOpinionGateTests {
		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
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
						ActionId = "control_gated_card",
						OwnerType = "country",
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
					new ActionDefinition {
						ActionId = "candidate_gated_card",
						OwnerType = "country",
						Conditions = new List<ExpressionNode> {
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
						ActionId = "high_control_gated_card",
						OwnerType = "country",
						Conditions = new List<ExpressionNode> {
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "control" },
									new ExpressionNode { Type = "value", Value = 50 }
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
					}
				}
			};
		}

		static World BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, int opinion) {
			var world = new World();
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());

			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1), IsPaused = false, MultiplierIndex = 0 });

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = "char1", CountryId = "Prussia", OrgId = "", RoleId = "diplomacy_advisor",
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner("char1", OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = "opinion_OrgA", Value = opinion });

			int cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "make_friend" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			int controlCardEntity = world.Create();
			world.Add(controlCardEntity, new GameAction { ActionId = "control_gated_card" });
			world.Add(controlCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(controlCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(controlCardEntity, new CardInHand { SlotIndex = 1 });
			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = "OrgA", CountryId = "Prussia", Value = 20, EffectId = "test_control" });

			int candidateCardEntity = world.Create();
			world.Add(candidateCardEntity, new GameAction { ActionId = "candidate_gated_card" });
			world.Add(candidateCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(candidateCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(candidateCardEntity, new CardInHand { SlotIndex = 2 });

			int highControlCardEntity = world.Create();
			world.Add(highControlCardEntity, new GameAction { ActionId = "high_control_gated_card" });
			world.Add(highControlCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(highControlCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(highControlCardEntity, new CardInHand { SlotIndex = 3 });

			return world;
		}

		[Fact]
		void make_friend_reports_unplayable_when_opinion_below_threshold() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 10);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "make_friend");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
		}

		[Fact]
		void make_friend_reports_playable_when_opinion_at_threshold_and_suitable_target_exists() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 30);
			int e = world.Create();
			world.Add(e, new Country("Austria"));

			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "make_friend");
			Assert.NotNull(entry);
			Assert.False(entry!.IsUnplayable);
		}

		[Fact]
		void existing_control_gated_card_playability_unaffected_by_opinion_wiring() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 0);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "control_gated_card");
			Assert.NotNull(entry);
			Assert.False(entry!.IsUnplayable);
		}

		[Fact]
		void make_friend_reports_insufficient_opinion_reason_when_opinion_below_threshold() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 10);
			int e = world.Create();
			world.Add(e, new Country("Austria"));

			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "make_friend");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
		}

		[Fact]
		void candidate_gated_card_reports_no_suitable_target_reason_when_no_candidate_exists() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 0);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "candidate_gated_card");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("no_suitable_target", entry.UnplayableReason);
		}

		[Fact]
		void control_gated_card_reports_insufficient_control_reason_when_control_below_threshold() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 0);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "high_control_gated_card");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("insufficient_control", entry.UnplayableReason);
		}

		static World BuildWorldWithStopFriendshipCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, bool relationStillHolds) {
			var world = new World();
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());
			world.Add(world.Create(), new Country("Austria"));

			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1), IsPaused = false, MultiplierIndex = 0 });

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = "char1", CountryId = "Prussia", OrgId = "", RoleId = "diplomacy_advisor",
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner("char1", OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = "opinion_OrgA", Value = 80 });

			if (relationStillHolds) {
				CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			}

			int cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "stop_friendship" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });
			world.Add(cardEntity, new RelationCardTarget { TargetCountryId = "Austria", Kind = RelationKind.Friend });

			return world;
		}

		[Fact]
		void stop_friendship_reports_unplayable_when_named_relation_no_longer_holds() {
			var world = BuildWorldWithStopFriendshipCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, relationStillHolds: false);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "stop_friendship");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
		}

		[Fact]
		void stop_friendship_reports_playable_when_named_relation_still_holds() {
			var world = BuildWorldWithStopFriendshipCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, relationStillHolds: true);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "stop_friendship");
			Assert.NotNull(entry);
			Assert.False(entry!.IsUnplayable);
		}

		static ActionCardEntry? FindEntry(IReadOnlyList<ActionCardEntry> entries, string actionId) {
			foreach (var e in entries) {
				if (e.ActionId == actionId) { return e; }
			}
			return null;
		}
	}
}
