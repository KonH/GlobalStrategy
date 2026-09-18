using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterCountryActionsOpinionGateTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static ActionConfig BuildActionConfig() {
			return TestActionConfig.Create()
				.HandSize("country", 3)
				.Action("make_friend", targetRole: "diplomacy_advisor", conditions: new[] {
					Expr.Gte("opinion", 30),
					Expr.HasRelation("none", "friend")
				})
				.Action("control_gated_card", Expr.Gte("control", 10))
				.Action("candidate_gated_card", Expr.HasRelation("none", "friend"))
				.Action("high_control_gated_card", Expr.Gte("control", 50))
				.Action("decrease_enemy_control",
					Expr.Gt(Expr.Sub(Expr.Field("totalCountryControl"), Expr.Field("control")), Expr.Value(0)))
				.Action("stop_friendship", targetRole: "diplomacy_advisor", conditions: new[] {
					Expr.Gte("opinion", 80),
					Expr.HasRelation("friend")
				})
				.Action("sell_arms", targetRole: "military_advisor", conditions: new[] {
					Expr.Gte("opinion", 80)
				})
				.Action("declare_revenge_war", targetRole: "military_advisor", conditions: new[] {
					Expr.Gte("control", 20),
					Expr.Gte("opinion", 25),
					Expr.Gte("warFree", 1)
				})
				.Build();
		}

		/// <summary>
		/// Prussia selected, one diplomacy advisor whose opinion is under test, and one card per
		/// gate variant so a single projection can be asserted from several angles.
		/// </summary>
		static TestWorld BuildWorldWithSelectedCountry(int opinion) {
			return TestWorld.CreateWithSelectedCountry()
				.Character("char1", roleId: "diplomacy_advisor")
				.Opinion("char1", opinion)
				.Card("make_friend", slotIndex: 0)
				.Card("control_gated_card", slotIndex: 1)
				.Control(value: 20, effectId: "test_control")
				.Card("candidate_gated_card", slotIndex: 2)
				.Card("high_control_gated_card", slotIndex: 3)
				.Card("decrease_enemy_control", slotIndex: 4);
		}

		VisualStateProbe Probe(
			CountryConfig? countryConfig = null) {
			return new VisualStateProbe(
				BuildActionConfig(), _resources, _relations, countryConfig: countryConfig);
		}

		[Fact]
		void make_friend_reports_unplayable_when_opinion_below_threshold() {
			var world = BuildWorldWithSelectedCountry(opinion: 10);

			VisualStateProbe probe = Probe().Update(world);

			Assert.True(probe.Card("make_friend").IsUnplayable);
		}

		[Fact]
		void make_friend_reports_playable_when_opinion_at_threshold_and_suitable_target_exists() {
			var world = BuildWorldWithSelectedCountry(opinion: 30).Country("Austria");

			VisualStateProbe probe = Probe().Update(world);

			Assert.False(probe.Card("make_friend").IsUnplayable);
		}

		[Fact]
		void ordinary_card_lists_playable_countries_in_config_order_without_binding_to_selection() {
			var world = TestWorld.CreateWithSelectedCountry()
				.Countries("Austria", "Germany")
				.Card("control_gated_card", slotIndex: 0, countryId: null)
				.As("card")
				.Control("Austria", value: 10, effectId: "austria_control")
				.Control("Germany", value: 10, effectId: "germany_control");
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = "Germany", IsAvailable = true }
				}
			};

			VisualStateProbe probe = Probe(countryConfig).Update(world);

			ActionCardEntry entry = Assert.Single(
				probe.CountryHand,
				candidate => candidate.ActionId == "control_gated_card");
			Assert.False(entry.CanPlay);
			// Hard-only badge ignores soft control gates, so every available country appears.
			Assert.Equal(new[] { "Austria", "Prussia", "Germany" }, entry.PlayableCountryIds);
			Assert.False(world.Has<CountryContext>(world.Id("card")));
		}

		[Fact]
		void existing_control_gated_card_playability_unaffected_by_opinion_wiring() {
			var world = BuildWorldWithSelectedCountry(opinion: 0);

			VisualStateProbe probe = Probe().Update(world);

			Assert.False(probe.Card("control_gated_card").IsUnplayable);
		}

		[Fact]
		void make_friend_reports_insufficient_opinion_reason_when_opinion_below_threshold() {
			var world = BuildWorldWithSelectedCountry(opinion: 10).Country("Austria");

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("make_friend");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
		}

		[Fact]
		void candidate_gated_card_reports_no_suitable_target_reason_when_no_candidate_exists() {
			var world = BuildWorldWithSelectedCountry(opinion: 0);

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("candidate_gated_card");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("no_friend_candidate", entry.UnplayableReason);
		}

		[Fact]
		void control_gated_card_reports_insufficient_control_reason_when_control_below_threshold() {
			var world = BuildWorldWithSelectedCountry(opinion: 0);

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("high_control_gated_card");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("insufficient_control", entry.UnplayableReason);
		}

		[Fact]
		void decrease_enemy_control_reports_no_enemy_control_for_nested_total_control_condition() {
			var world = BuildWorldWithSelectedCountry(opinion: 0);

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("decrease_enemy_control");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("no_enemy_control", entry.UnplayableReason);
		}

		TestWorld BuildWorldWithStopFriendshipCard(bool relationStillHolds) {
			var world = TestWorld.CreateWithSelectedCountry()
				.Country("Austria")
				.Character("char1", roleId: "diplomacy_advisor")
				.Opinion("char1", 80);
			if (relationStillHolds) {
				_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			}
			return world
				.Card("stop_friendship")
				.With(new RelationCardTarget { TargetCountryId = "Austria", Kind = RelationKind.Friend });
		}

		[Fact]
		void stop_friendship_reports_unplayable_when_named_relation_no_longer_holds() {
			TestWorld world = BuildWorldWithStopFriendshipCard(relationStillHolds: false);

			VisualStateProbe probe = Probe().Update(world);

			Assert.True(probe.Card("stop_friendship").IsUnplayable);
		}

		[Fact]
		void stop_friendship_reports_playable_when_named_relation_still_holds() {
			TestWorld world = BuildWorldWithStopFriendshipCard(relationStillHolds: true);

			VisualStateProbe probe = Probe().Update(world);

			Assert.False(probe.Card("stop_friendship").IsUnplayable);
		}

		static TestWorld BuildWorldWithSellArmsCard(int militaryOpinion, int diplomacyOpinion) {
			return TestWorld.CreateWithSelectedCountry()
				.Character("military", roleId: "military_advisor")
				.Opinion("military", militaryOpinion)
				.Character("diplomat", roleId: "diplomacy_advisor")
				.Opinion("diplomat", diplomacyOpinion)
				.Card("sell_arms")
				.As("card");
		}

		[Fact]
		void sell_arms_stays_playable_in_peacetime_with_sufficient_military_opinion() {
			TestWorld world = BuildWorldWithSellArmsCard(militaryOpinion: 80, diplomacyOpinion: 100);

			VisualStateProbe probe = Probe().Update(world);

			Assert.False(probe.Card("sell_arms").IsUnplayable);
			Assert.True(world.Has<CardInHand>(world.Id("card")));
		}

		[Fact]
		void sell_arms_reports_insufficient_opinion_without_removing_held_card() {
			TestWorld world = BuildWorldWithSellArmsCard(militaryOpinion: 79, diplomacyOpinion: 100);

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("sell_arms");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
			Assert.True(world.Has<CardInHand>(world.Id("card")));
		}

		[Fact]
		void sell_arms_visual_and_play_pipeline_use_military_advisor_opinion() {
			ActionConfig config = BuildActionConfig();
			TestWorld world = BuildWorldWithSellArmsCard(militaryOpinion: 79, diplomacyOpinion: 100);

			var probe = new VisualStateProbe(config, _resources, _relations).Update(world);
			bool pipelineVerdict = ActionPlayability.Evaluate(
				world, config, world.Id("card"), "sell_arms", "OrgA", "Prussia", _resources, _relations);

			ActionCardEntry entry = probe.Card("sell_arms");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("insufficient_opinion", entry.UnplayableReason);
			Assert.False(pipelineVerdict);
		}

		TestWorld BuildWorldWithRevengeCard(bool atWar) {
			var world = TestWorld.CreateWithSelectedCountry()
				.Character("mil1", roleId: "military_advisor")
				.Opinion("mil1", 25)
				.Control(value: 20, effectId: "test_control");
			if (atWar) {
				Wars.DeclareWar(world, _resources, "Prussia", "Austria", TestWorld.DefaultTime);
			}
			return world.Card("declare_revenge_war");
		}

		[Fact]
		void revenge_reports_at_war_reason_when_war_free_fails() {
			TestWorld world = BuildWorldWithRevengeCard(atWar: true);

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("declare_revenge_war");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("at_war", entry.UnplayableReason);
		}

		[Fact]
		void revenge_is_playable_when_control_opinion_and_war_free_all_hold() {
			TestWorld world = BuildWorldWithRevengeCard(atWar: false);

			VisualStateProbe probe = Probe().Update(world);

			Assert.False(probe.Card("declare_revenge_war").IsUnplayable);
		}

		[Fact]
		void revenge_reports_insufficient_control_reason_before_war_free_when_both_fail() {
			TestWorld world = BuildWorldWithRevengeCard(atWar: true);
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.World.GetMatchingArchetypes(req, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					controls[i].Value = 5;
				}
			}

			VisualStateProbe probe = Probe().Update(world);

			ActionCardEntry entry = probe.Card("declare_revenge_war");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("insufficient_control", entry.UnplayableReason);
		}
	}
}
