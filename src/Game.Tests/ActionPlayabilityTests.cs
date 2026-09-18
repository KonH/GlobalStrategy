using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using Xunit;

namespace GS.Game.Tests {
	public class ActionPlayabilityTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static ActionConfig BuildActionConfig() {
			return TestActionConfig.Create()
				.Action("org_card", ownerType: "org", cost: Gold(50))
				.Action("country_card", conditions: new[] { Expr.Gte("control", 10) }, cost: Gold(20))
				.Action("make_friend", targetRole: "diplomacy_advisor", cost: Gold(50), conditions: new[] {
					Expr.Gte("opinion", 30),
					Expr.HasRelation("none", "friend")
				})
				.Action("stop_friendship", targetRole: "diplomacy_advisor", cost: Gold(100), conditions: new[] {
					Expr.Gte("opinion", 80),
					Expr.HasRelation("friend")
				})
				.Action("decrease_enemy_control", cost: Gold(250), conditions: new[] {
					Expr.Gt(Expr.Sub(Expr.Field("totalCountryControl"), Expr.Field("control")), Expr.Value(0))
				})
				.Action("force_war_win", targetRole: "military_advisor", cost: Gold(300), conditions: new[] {
					Expr.Gte("control", 10),
					Expr.Gte("opinion", 50),
					Expr.Gte("isInWar", 1),
					Expr.Gte("warProgress", 50)
				})
				.Action("force_war_loss", targetRole: "military_advisor", cost: Gold(500), conditions: new[] {
					Expr.Gte("control", 20),
					Expr.Gte("opinion", 80),
					Expr.Gte("isInWar", 1),
					Expr.Lte("warProgress", 0)
				})
				.Action("sell_arms", targetRole: "military_advisor", conditions: new[] {
					Expr.Gte("opinion", 80)
				})
				.Action("declare_revenge_war", targetRole: "military_advisor", cost: Gold(50), conditions: new[] {
					Expr.Gte("control", 20),
					Expr.Gte("opinion", 25),
					Expr.Gte("warFree", 1),
					Expr.Gte("revengeEligible", 1)
				})
				.Build();
		}

		static ActionCost[] Gold(double amount) {
			return new[] { new ActionCost { ResourceId = "gold", Amount = amount } };
		}

		static int AddCountry(TestWorld world, string countryId) {
			return world.Country(countryId).Last;
		}

		static int AddAdvisor(TestWorld world, string countryId, string charId, string orgId, string roleId, int opinion) {
			int charEntity = world.Character(charId, countryId, roleId).Last;
			world.Opinion(charId, opinion, orgId);
			return charEntity;
		}

		static int AddMilitaryAdvisor(TestWorld world, string countryId, string charId, string orgId, int opinion) {
			return AddAdvisor(world, countryId, charId, orgId, "military_advisor", opinion);
		}

		static void SetWarProgress(TestWorld world, ResourceQuery resources, double value) {
			int[] required = { TypeId<War>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				var wars = arch.GetColumn<War>();
				for (int i = 0; i < arch.Count; i++) {
					ResourceMutations.TrySetValue(resources, world, wars[i].WarId, ResourceDefinitions.WarProgress, value, out _);
				}
			}
		}

		static int AddDiplomacyAdvisor(TestWorld world, string countryId, string charId, string orgId, int opinion) {
			return AddAdvisor(world, countryId, charId, orgId, "diplomacy_advisor", opinion);
		}

		static int AddGold(TestWorld world, string orgId, double amount) {
			return world.Resource(orgId, "gold", amount).Last;
		}

		static void AddControl(TestWorld world, string orgId, string countryId, int value) {
			world.Control(countryId, value, orgId, "test_control");
		}

		/// <summary>A card already in hand and marked for use this tick; org-owned when countryId is null.</summary>
		static int AddCard(TestWorld world, string orgId, string actionId, string? countryId) {
			return world
				.Card(actionId,
					orgId: orgId,
					countryId: countryId,
					kind: countryId == null ? CardOwnerKind.Org : CardOwnerKind.Country)
				.With(new CardUse { CountryId = countryId ?? "" })
				.Last;
		}

		static int AddRelationCard(TestWorld world, string orgId, string actionId, string countryId, string targetCountryId, RelationKind kind) {
			int e = AddCard(world, orgId, actionId, countryId);
			world.Add(e, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = kind });
			return e;
		}

		static bool? RunPipeline(TestWorld world, ActionConfig config, ResourceQuery resources, CountryRelations relations, int entity, DateTime currentTime = default) {
			CheckActionConditionSystem.Update(world, config, resources, relations, null, currentTime);
			DeductActionCostSystem.Update(world, config, resources);
			ActionSucceededSystem.Update(world, config);
			if (world.Has<ActionSucceeded>(entity)) { return true; }
			if (world.Has<ActionFailed>(entity)) { return false; }
			return null;
		}

		static int AddCooldown(TestWorld world, string orgId, string actionId, DateTime endTime) {
			return world.Entity()
				.With(new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime })
				.Last;
		}

		[Fact]
		void evaluate_verdict_matches_pipeline_action_valid_outcome() {
			var config = BuildActionConfig();

			// org card, no country, affordable -> playable.
			var worldA = TestWorld.Create();
			AddGold(worldA, "OrgA", 100.0);
			int cardA = AddCard(worldA, "OrgA", "org_card", null);
			bool expectedA = ActionPlayability.Evaluate(worldA, config, -1, "org_card", "OrgA", null, _resources, _relations);
			Assert.Equal(expectedA, RunPipeline(worldA, config, _resources, _relations, cardA));
			Assert.True(expectedA);

			// org card, unaffordable -> unplayable.
			var worldB = TestWorld.Create();
			AddGold(worldB, "OrgA", 10.0);
			int cardB = AddCard(worldB, "OrgA", "org_card", null);
			bool expectedB = ActionPlayability.Evaluate(worldB, config, -1, "org_card", "OrgA", null, _resources, _relations);
			Assert.Equal(expectedB, RunPipeline(worldB, config, _resources, _relations, cardB));
			Assert.False(expectedB);

			// country card, control-threshold condition met and affordable -> playable.
			var worldC = TestWorld.Create();
			AddGold(worldC, "OrgA", 100.0);
			AddControl(worldC, "OrgA", "Prussia", 10);
			int cardC = AddCard(worldC, "OrgA", "country_card", "Prussia");
			bool expectedC = ActionPlayability.Evaluate(worldC, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations);
			Assert.Equal(expectedC, RunPipeline(worldC, config, _resources, _relations, cardC));
			Assert.True(expectedC);

			// country card, control-threshold condition unmet -> unplayable.
			var worldD = TestWorld.Create();
			AddGold(worldD, "OrgA", 100.0);
			AddControl(worldD, "OrgA", "Prussia", 5);
			int cardD = AddCard(worldD, "OrgA", "country_card", "Prussia");
			bool expectedD = ActionPlayability.Evaluate(worldD, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations);
			Assert.Equal(expectedD, RunPipeline(worldD, config, _resources, _relations, cardD));
			Assert.False(expectedD);

			// unknown actionId -> false; cannot be represented as a played card at all, direct call only.
			Assert.False(ActionPlayability.Evaluate(worldD, config, -1, "does_not_exist", "OrgA", null, _resources, _relations));
		}

		[Fact]
		void unaffordable_play_still_discards_card_and_deducts_nothing() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			int goldEntity = AddGold(world, "OrgA", 5.0);
			AddControl(world, "OrgA", "Prussia", 10);
			int card = AddCard(world, "OrgA", "country_card", "Prussia");

			bool expected = ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations);
			Assert.False(expected);

			CheckActionConditionSystem.Update(world, config, _resources, _relations);
			DeductActionCostSystem.Update(world, config, _resources);
			ActionSucceededSystem.Update(world, config);
			RemoveCardFromHandSystem.Update(world);

			Assert.True(world.Has<ActionFailed>(card));
			Assert.False(world.Has<ActionSucceeded>(card));
			Assert.Equal(5.0, world.Get<Resource>(goldEntity).Value);
			Assert.False(world.Has<CardInHand>(card));
			Assert.True(world.Has<CardDiscard>(card));
		}

		[Fact]
		void deduct_uses_same_resource_entity_lookup_as_affordability() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			int goldA = AddGold(world, "OrgA", 100.0);
			int goldB = AddGold(world, "OrgB", 100.0);
			AddCard(world, "OrgA", "org_card", null);

			CheckActionConditionSystem.Update(world, config, _resources, _relations);
			DeductActionCostSystem.Update(world, config, _resources);

			int found = _resources.FindEntity(world, "OrgA", "gold");
			Assert.Equal(goldA, found);
			Assert.Equal(50.0, world.Get<Resource>(goldA).Value);
			Assert.Equal(100.0, world.Get<Resource>(goldB).Value);
		}

		[Fact]
		void make_friend_unplayable_when_opinion_below_threshold_even_with_suitable_target() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 29);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void make_friend_unplayable_when_no_suitable_target_even_with_high_opinion() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void make_friend_playable_when_opinion_at_threshold_and_suitable_target_exists() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 30);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void make_friend_unaffordable_despite_gates_satisfied_is_unplayable() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 10.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void existing_control_gated_card_unaffected_by_opinion_wiring() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);

			// No diplomacy advisor/opinion/relation data seeded at all ÔÇö control-only card must
			// still evaluate purely off Control, unaffected by the new Opinion/HasSuitableRelationTarget wiring.
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void stop_friendship_unplayable_when_named_relation_no_longer_holds() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// No Friend relation ever set between Prussia and Austria ÔÇö the named relation is dead.
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void stop_friendship_playable_when_named_relation_still_holds_opinion_and_cost_met() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.True(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void stop_friendship_unplayable_when_opinion_below_threshold_even_if_relation_still_holds() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 79);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void stop_friendship_unplayable_when_unaffordable_even_if_gates_satisfied() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 99.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void stop_friendship_dead_instance_stays_unplayable_even_when_a_different_relation_of_same_kind_exists() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddCountry(world, "Bavaria");
			AddDiplomacyAdvisor(world, "Prussia", "char1", "OrgA", opinion: 80);
			// A Friend relation with a *different* country exists, but this instance names Austria specifically.
			_relations.SetRelation(world, "Prussia", "Bavaria", RelationKind.Friend);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "Austria", RelationKind.Friend);

			Assert.False(ActionPlayability.Evaluate(world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void entity_negative_one_with_relation_agnostic_conditions_does_not_throw() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);

			// Regression guard: entity == -1 must never attempt World.Has<RelationCardTarget>(-1),
			// which would throw IndexOutOfRangeException without the entity >= 0 guard.
			var exception = Record.Exception(() => ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations));
			Assert.Null(exception);
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void decrease_enemy_control_unplayable_when_no_other_org_holds_control() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 250.0);
			AddControl(world, "OrgA", "Prussia", 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void decrease_enemy_control_playable_when_another_org_holds_control_and_affordable() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 250.0);
			AddControl(world, "OrgB", "Prussia", 10);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void decrease_enemy_control_unplayable_when_unaffordable_even_with_enemy_control_present() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 249.0);
			AddControl(world, "OrgB", "Prussia", 10);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "decrease_enemy_control", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_unplayable_when_not_in_any_war() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_playable_when_in_war_and_thresholds_met_and_affordable() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 50);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_unplayable_when_control_one_below_gate() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddControl(world, "OrgA", "Prussia", 9);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_unplayable_when_opinion_one_below_gate() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 49);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 50);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_unplayable_when_own_war_progress_one_below_gate() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddControl(world, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 49);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_own_progress_is_signed_relative_to_attacker_or_defender() {
			var config = BuildActionConfig();

			// Prussia is the defender: raw WarProgress.Value = -60 -> own progress = 60 -> playable.
			var defenderWorld = TestWorld.Create();
			AddGold(defenderWorld, "OrgA", 300.0);
			AddControl(defenderWorld, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(defenderWorld, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(defenderWorld, _resources, "France", "Prussia", new DateTime(1880, 1, 1));
			SetWarProgress(defenderWorld, _resources, -60);
			Assert.True(ActionPlayability.Evaluate(defenderWorld, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));

			// Prussia is the attacker with the same raw value: own progress = -60 -> not playable.
			var attackerWorld = TestWorld.Create();
			AddGold(attackerWorld, "OrgA", 300.0);
			AddControl(attackerWorld, "OrgA", "Prussia", 10);
			AddMilitaryAdvisor(attackerWorld, "Prussia", "char1", "OrgA", opinion: 50);
			Wars.DeclareWar(attackerWorld, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(attackerWorld, _resources, -60);
			Assert.False(ActionPlayability.Evaluate(attackerWorld, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_is_playable_and_surrender_is_not_when_winning() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 500.0);
			AddControl(world, "OrgA", "Prussia", 25);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 90);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 60);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_loss", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void surrender_is_playable_and_ultimatum_is_not_when_losing() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 500.0);
			AddControl(world, "OrgA", "Prussia", 25);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 90);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, -40);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "force_war_loss", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void ultimatum_and_diplomacy_gated_card_evaluate_opinion_independently_per_role() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 300.0);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddControl(world, "OrgA", "Prussia", 10);
			// Diplomacy advisor's opinion is high enough for make_friend but the military advisor's is not.
			AddDiplomacyAdvisor(world, "Prussia", "diplo1", "OrgA", opinion: 50);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 10);
			Wars.DeclareWar(world, _resources, "Prussia", "France", new DateTime(1880, 1, 1));
			SetWarProgress(world, _resources, 50);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "make_friend", "OrgA", "Prussia", _resources, _relations));
			Assert.False(ActionPlayability.Evaluate(world, config, -1, "force_war_win", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void sell_arms_requires_military_advisor_opinion_regardless_of_war() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddAdvisor(world, "Prussia", "diplomat", "OrgA", "diplomacy_advisor", 100);
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 79);

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "sell_arms", "OrgA", "Prussia", _resources, _relations));

			Wars.DeclareWar(world, _resources, "Prussia", "Austria", new System.DateTime(1880, 1, 1));
			Assert.False(ActionPlayability.Evaluate(world, config, -1, "sell_arms", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void sell_arms_is_playable_at_exact_opinion_threshold_in_peacetime_without_gold() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 80);

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "sell_arms", "OrgA", "Prussia", _resources, _relations));
		}

		[Fact]
		void held_sell_arms_card_stays_in_hand_and_stays_playable_across_war_transitions() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 80);
			int card = AddCard(world, "OrgA", "sell_arms", "Prussia");

			Assert.True(ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations));

			Wars.DeclareWar(world, _resources, "Prussia", "Austria", new System.DateTime(1880, 1, 1));
			Assert.True(ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations));
			Assert.True(world.Has<CardInHand>(card));

			Wars.StopWar(
				world,
				_resources,
				"Prussia",
				new System.DateTime(1880, 1, 1),
				new System.Random(1),
				new GameSettings(),
				new ProvinceTopology(new ProvinceConfig()),
				new Dictionary<string, (double Lon, double Lat)>(),
				100);
			Assert.True(ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations));
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void held_sell_arms_card_stays_unplayable_for_opinion_after_war_ends() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 79);
			int card = AddCard(world, "OrgA", "sell_arms", "Prussia");
			Wars.DeclareWar(world, _resources, "Prussia", "Austria", new System.DateTime(1880, 1, 1));

			Assert.False(ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations));

			Wars.StopWar(
				world,
				_resources,
				"Prussia",
				new System.DateTime(1880, 1, 1),
				new System.Random(1),
				new GameSettings(),
				new ProvinceTopology(new ProvinceConfig()),
				new Dictionary<string, (double Lon, double Lat)>(),
				100);
			Assert.False(ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations));
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void sell_arms_playability_matches_condition_pipeline() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddAdvisor(world, "Prussia", "general", "OrgA", "military_advisor", 80);
			int card = AddCard(world, "OrgA", "sell_arms", "Prussia");

			bool expected = ActionPlayability.Evaluate(world, config, card, "sell_arms", "OrgA", "Prussia", _resources, _relations);

			Assert.True(expected);
			Assert.Equal(expected, RunPipeline(world, config, _resources, _relations, card));
		}

		[Fact]
		void revenge_unplayable_when_control_below_threshold() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 19);
			AddMilitaryAdvisor(world, "Prussia", "char1", "OrgA", opinion: 50);
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_unplayable_when_opinion_below_threshold_at_military_advisor_even_with_high_diplomacy_advisor_opinion() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 20);
			AddDiplomacyAdvisor(world, "Prussia", "diplo1", "OrgA", opinion: 90);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 24);
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_unplayable_when_the_selected_country_is_at_war() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Prussia", "Austria", new DateTime(1880, 1, 1));
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_unplayable_when_the_orgs_hq_country_is_at_war() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 50);
			Wars.DeclareWar(world, _resources, "Great_Britain", "Austria", new DateTime(1880, 1, 1));
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_unplayable_when_no_prior_war_loss_against_the_country() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Great_Britain", 20);
			AddMilitaryAdvisor(world, "Great_Britain", "mil1", "OrgA", opinion: 25);
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_playable_when_all_conditions_hold_and_affordable() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Great_Britain", 20);
			AddMilitaryAdvisor(world, "Great_Britain", "mil1", "OrgA", opinion: 25);
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			int card = AddCard(world, "OrgA", "declare_revenge_war", "Great_Britain");
			world.Add(card, new RevengeCardTarget { TargetCountryId = "Prussia" });
			Assert.True(ActionPlayability.Evaluate(world, config, card, "declare_revenge_war", "OrgA", "Great_Britain", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void revenge_unplayable_again_once_the_loss_has_been_avenged() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 20);
			AddMilitaryAdvisor(world, "Prussia", "mil1", "OrgA", opinion: 25);
			var hqCountryByOrgId = new Dictionary<string, string> { ["OrgA"] = "Great_Britain" };
			RevengeEligibilityQuery.SetEligible(world, "Great_Britain", "Prussia");

			RevengeEligibilityQuery.OnWarResolved(world, winnerCountryId: "Great_Britain", loserCountryId: "Prussia");

			Assert.False(ActionPlayability.Evaluate(world, config, -1, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations, hqCountryByOrgId));
		}

		[Fact]
		void country_card_cycles_playable_unplayable_playable_again_across_a_seeded_cooldown() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);
			var start = new DateTime(1880, 1, 1);

			// No tracking entity yet -> playable.
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations, currentTime: start));

			// Seed a cooldown with a future EndTime -> unplayable.
			var endTime = start.AddDays(7);
			AddCooldown(world, "OrgA", "country_card", endTime);
			Assert.False(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations, currentTime: start));

			// currentTime still before EndTime -> still unplayable.
			Assert.False(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations, currentTime: endTime.AddDays(-1)));

			// currentTime passes EndTime -> playable again.
			Assert.True(ActionPlayability.Evaluate(world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations, currentTime: endTime.AddDays(1)));
		}

		[Fact]
		void org_owned_card_unaffected_by_country_action_cooldown_seeded_for_same_org() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			var start = new DateTime(1880, 1, 1);

			// Cooldown seeded for a *different* actionId (a country card), same org.
			AddCooldown(world, "OrgA", "country_card", start.AddDays(7));

			Assert.True(ActionPlayability.Evaluate(world, config, -1, "org_card", "OrgA", null, _resources, _relations, currentTime: start));
		}

		[Fact]
		void evaluate_verdict_matches_pipeline_action_valid_outcome_when_on_cooldown() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100.0);
			AddControl(world, "OrgA", "Prussia", 10);
			var start = new DateTime(1880, 1, 1);
			AddCooldown(world, "OrgA", "country_card", start.AddDays(7));
			int card = AddCard(world, "OrgA", "country_card", "Prussia");

			bool expected = ActionPlayability.Evaluate(world, config, card, "country_card", "OrgA", "Prussia", _resources, _relations, currentTime: start);

			Assert.False(expected);
			Assert.Equal(expected, RunPipeline(world, config, _resources, _relations, card, start));
		}

		[Fact]
		void canonical_result_orders_authored_capacity_cooldown_and_aggregated_gold_once() {
			// Two separate gold costs on purpose - they must aggregate into a single entry.
			ActionConfig config = TestActionConfig.Create()
				.Action("improve_control",
					conditions: new[] { Expr.Gte("control", 10), Expr.HasRelation("none", "friend") },
					cost: new[] {
						new ActionCost { ResourceId = "gold", Amount = 50 },
						new ActionCost { ResourceId = "gold", Amount = 25 }
					})
				.Build();
			var world = TestWorld.Create();
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddControl(world, "OrgA", "Prussia", 10);
			AddGold(world, "OrgA", 70);

			ActionPlayabilityResult result = ActionPlayability.Evaluate(
				world, config, -1, "improve_control", "OrgA", "Prussia", _resources, _relations);

			Assert.Equal(new[] {
				"action.requirement.control_min",
				"action.requirement.friend_candidate",
				"action.requirement.control_capacity",
				"action.requirement.cooldown_ready",
				"action.requirement.gold"
			}, System.Linq.Enumerable.Select(result.Entries, entry => entry.LocaleKey));
			Assert.Single(System.Linq.Enumerable.Where(result.Entries, entry => entry.LocaleKey == "action.requirement.gold"));
			Assert.Equal("75", result.Entries[4].LocaleArguments[0]);
			Assert.False(result.CanPlay);
			Assert.Same(result.Entries[4], result.FirstFailure);
		}

		[Fact]
		void relation_card_is_playable_for_any_selected_country_that_still_holds_the_relation() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100);
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddCountry(world, "France");
			AddDiplomacyAdvisor(world, "Austria", "austria_diplomat", "OrgA", 80);
			_relations.SetRelation(world, "Austria", "France", RelationKind.Friend);
			// Card instance was created for Prussia's friendship pair, but Austria currently
			// holds the France friendship ÔÇö selected-country soft/hard gates decide playability.
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "France", RelationKind.Friend);

			ActionPlayabilityResult result = ActionPlayability.Evaluate(
				world, config, card, "stop_friendship", "OrgA", "Austria", _resources, _relations);

			Assert.DoesNotContain(
				result.Entries,
				entry => entry.LocaleKey == "action.requirement.primary_country");
			Assert.True(result.CanPlay);
		}

		[Fact]
		void evaluate_false_when_direct_country_target_is_destroyed() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100);
			AddControl(world, "OrgA", "Prussia", 10);
			int dead = AddCountry(world, "Prussia");
			world.Add(dead, new IsDestroyed());

			ActionPlayabilityResult result = ActionPlayability.Evaluate(
				world, config, -1, "country_card", "OrgA", "Prussia", _resources, _relations);

			Assert.False(result.CanPlay);
			Assert.Equal("country_no_longer_exists", result.FirstFailure!.ReasonCode);
			Assert.Equal("action.country.unplayable.country_no_longer_exists", result.FirstFailure.LocaleKey);
		}

		[Fact]
		void evaluate_false_when_relation_card_target_is_destroyed() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 100);
			AddCountry(world, "Prussia");
			int dead = AddCountry(world, "France");
			world.Add(dead, new IsDestroyed());
			AddDiplomacyAdvisor(world, "Prussia", "prussia_diplomat", "OrgA", 80);
			int card = AddRelationCard(world, "OrgA", "stop_friendship", "Prussia", "France", RelationKind.Friend);

			ActionPlayabilityResult result = ActionPlayability.Evaluate(
				world, config, card, "stop_friendship", "OrgA", "Prussia", _resources, _relations);

			Assert.False(result.CanPlay);
			Assert.Equal("country_no_longer_exists", result.FirstFailure!.ReasonCode);
		}

		[Fact]
		void evaluate_false_when_revenge_card_target_is_destroyed() {
			var config = BuildActionConfig();
			var world = TestWorld.Create();
			AddGold(world, "OrgA", 1000);
			AddCountry(world, "Prussia");
			int dead = AddCountry(world, "Austria");
			world.Add(dead, new IsDestroyed());
			int card = AddCard(world, "OrgA", "declare_revenge_war", "Prussia");
			world.Add(card, new RevengeCardTarget { TargetCountryId = "Austria" });

			ActionPlayabilityResult result = ActionPlayability.Evaluate(
				world, config, card, "declare_revenge_war", "OrgA", "Prussia", _resources, _relations);

			Assert.False(result.CanPlay);
			Assert.Equal("country_no_longer_exists", result.FirstFailure!.ReasonCode);
		}
	}
}
