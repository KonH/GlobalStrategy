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
									new ExpressionNode { Type = "hasCountryRelation", RelationKind = "none", DesiredRelationKind = "friend" },
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
									new ExpressionNode { Type = "hasCountryRelation", RelationKind = "none", DesiredRelationKind = "friend" },
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
									new ExpressionNode { Type = "hasCountryRelation", RelationKind = "friend" },
									new ExpressionNode { Type = "value", Value = 1 }
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
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "control" },
									new ExpressionNode { Type = "value", Value = 20 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "opinion" },
									new ExpressionNode { Type = "value", Value = 25 }
								}
							},
							new ExpressionNode {
								Type = "gte",
								Members = new List<ExpressionNode> {
									new ExpressionNode { Type = "warFree" },
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
			world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			int controlCardEntity = world.Create();
			world.Add(controlCardEntity, new GameAction { ActionId = "control_gated_card" });
			world.Add(controlCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(controlCardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(controlCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(controlCardEntity, new CardInHand { SlotIndex = 1 });
			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = "OrgA", CountryId = "Prussia", Value = 20, EffectId = "test_control" });

			int candidateCardEntity = world.Create();
			world.Add(candidateCardEntity, new GameAction { ActionId = "candidate_gated_card" });
			world.Add(candidateCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(candidateCardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(candidateCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(candidateCardEntity, new CardInHand { SlotIndex = 2 });

			int highControlCardEntity = world.Create();
			world.Add(highControlCardEntity, new GameAction { ActionId = "high_control_gated_card" });
			world.Add(highControlCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(highControlCardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(highControlCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(highControlCardEntity, new CardInHand { SlotIndex = 3 });

			int decreaseEnemyControlCardEntity = world.Create();
			world.Add(decreaseEnemyControlCardEntity, new GameAction { ActionId = "decrease_enemy_control" });
			world.Add(decreaseEnemyControlCardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(decreaseEnemyControlCardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(decreaseEnemyControlCardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(decreaseEnemyControlCardEntity, new CardInHand { SlotIndex = 4 });

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
		void ordinary_card_lists_playable_countries_in_config_order_without_binding_to_selection() {
			var world = new World();
			int selected = world.Create();
			world.Add(selected, new Country("Prussia"));
			world.Add(selected, new IsSelected());
			int austria = world.Create();
			world.Add(austria, new Country("Austria"));
			int germany = world.Create();
			world.Add(germany, new Country("Germany"));
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int card = world.Create();
			world.Add(card, new GameAction { ActionId = "control_gated_card" });
			world.Add(card, new OrgContext { OrgId = "OrgA" });
			world.Add(card, new CardOwnerType(CardOwnerKind.Country));
			world.Add(card, new CardInHand { SlotIndex = 0 });
			int austriaControl = world.Create();
			world.Add(austriaControl, new ControlEffect {
				OrgId = "OrgA", CountryId = "Austria", Value = 10, EffectId = "austria_control"
			});
			int germanyControl = world.Create();
			world.Add(germanyControl, new ControlEffect {
				OrgId = "OrgA", CountryId = "Germany", Value = 10, EffectId = "germany_control"
			});
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = "Germany", IsAvailable = true }
				}
			};
			var state = new VisualState();
			var converter = new VisualStateConverter(
				state, BuildActionConfig(), countryConfig: countryConfig);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry entry = Assert.Single(
				state.SelectedCountry.CountryActions.Hand,
				candidate => candidate.ActionId == "control_gated_card");
			Assert.False(entry.CanPlay);
			Assert.Equal(new[] { "Austria", "Germany" }, entry.PlayableCountryIds);
			Assert.False(world.Has<CountryContext>(card));
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
			Assert.Equal("no_friend_candidate", entry.UnplayableReason);
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

		[Fact]
		void decrease_enemy_control_reports_no_enemy_control_for_nested_total_control_condition() {
			var world = BuildWorldWithSelectedCountry(out int gameTimeEntity, out int localeEntity, out int orgEntity, opinion: 0);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "decrease_enemy_control");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("no_enemy_control", entry.UnplayableReason);
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
			world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
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

		static World BuildWorldWithSellArmsCard(
			out int gameTimeEntity,
			out int localeEntity,
			out int orgEntity,
			out int cardEntity,
			int militaryOpinion,
			int diplomacyOpinion) {
			var world = new World();
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());

			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime {
				CurrentTime = new DateTime(1880, 1, 1),
				IsPaused = false,
				MultiplierIndex = 0
			});

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			AddCharacterWithOpinion(
				world,
				"military",
				"military_advisor",
				militaryOpinion);
			AddCharacterWithOpinion(
				world,
				"diplomat",
				"diplomacy_advisor",
				diplomacyOpinion);

			cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "sell_arms" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			return world;
		}

		static void AddCharacterWithOpinion(
			World world,
			string characterId,
			string roleId,
			int opinion) {
			int characterEntity = world.Create();
			world.Add(characterEntity, new Character {
				CharacterId = characterId,
				CountryId = "Prussia",
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});

			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(resourceEntity, new Resource {
				ResourceId = "opinion_OrgA",
				Value = opinion
			});
		}

		[Fact]
		void sell_arms_stays_playable_in_peacetime_with_sufficient_military_opinion() {
			var world = BuildWorldWithSellArmsCard(
				out int gameTimeEntity,
				out int localeEntity,
				out int orgEntity,
				out int cardEntity,
				militaryOpinion: 80,
				diplomacyOpinion: 100);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry? entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "sell_arms");
			Assert.NotNull(entry);
			Assert.False(entry!.IsUnplayable);
			Assert.True(world.Has<CardInHand>(cardEntity));
		}

		[Fact]
		void sell_arms_reports_insufficient_opinion_without_removing_held_card() {
			var world = BuildWorldWithSellArmsCard(
				out int gameTimeEntity,
				out int localeEntity,
				out int orgEntity,
				out int cardEntity,
				militaryOpinion: 79,
				diplomacyOpinion: 100);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry? entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "sell_arms");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
			Assert.True(world.Has<CardInHand>(cardEntity));
		}

		[Fact]
		void sell_arms_visual_and_play_pipeline_use_military_advisor_opinion() {
			ActionConfig config = BuildActionConfig();
			var world = BuildWorldWithSellArmsCard(
				out int gameTimeEntity,
				out int localeEntity,
				out int orgEntity,
				out int cardEntity,
				militaryOpinion: 79,
				diplomacyOpinion: 100);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, config);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry? entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "sell_arms");
			bool pipelineVerdict = ActionPlayability.Evaluate(
				world,
				config,
				cardEntity,
				"sell_arms",
				"OrgA",
				"Prussia");

			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
			Assert.False(pipelineVerdict);
		}

		static ActionCardEntry? FindEntry(IReadOnlyList<ActionCardEntry> entries, string actionId) {
			foreach (var e in entries) {
				if (e.ActionId == actionId) { return e; }
			}
			return null;
		}

		static World BuildWorldWithRevengeCard(
			out int gameTimeEntity, out int localeEntity, out int orgEntity, bool atWar) {
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
				CharacterId = "mil1", CountryId = "Prussia", OrgId = "", RoleId = "military_advisor",
				NamePartKeys = Array.Empty<string>()
			});
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner("mil1", OwnerType.Character));
			world.Add(resEntity, new Resource { ResourceId = "opinion_OrgA", Value = 25 });

			int controlEntity = world.Create();
			world.Add(controlEntity, new ControlEffect { OrgId = "OrgA", CountryId = "Prussia", Value = 20, EffectId = "test_control" });

			if (atWar) {
				Wars.DeclareWar(world, "Prussia", "Austria", new DateTime(1880, 1, 1));
			}

			int cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "declare_revenge_war" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			return world;
		}

		[Fact]
		void revenge_reports_at_war_reason_when_war_free_fails() {
			var world = BuildWorldWithRevengeCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, atWar: true);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_revenge_war");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("at_war", entry.UnplayableReason);
		}

		[Fact]
		void revenge_is_playable_when_control_opinion_and_war_free_all_hold() {
			var world = BuildWorldWithRevengeCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, atWar: false);
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_revenge_war");
			Assert.NotNull(entry);
			Assert.False(entry!.IsUnplayable);
		}

		[Fact]
		void revenge_reports_insufficient_control_reason_before_war_free_when_both_fail() {
			var world = BuildWorldWithRevengeCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, atWar: true);
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) { controls[i].Value = 5; }
			}
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_revenge_war");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("insufficient_control", entry.UnplayableReason);
		}
	}
}
