using System;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryActionConditionContextTests {
		static ActionDefinition Definition(string role = "military_advisor") {
			return new ActionDefinition {
				ActionId = "sell_arms",
				OwnerType = "country",
				TargetRole = role
			};
		}

		static int AddCharacter(World world, string countryId, string characterId, string roleId) {
			int entity = world.Create();
			world.Add(entity, new Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});
			return entity;
		}

		static void AddOpinion(World world, string characterId, string orgId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(entity, new Resource {
				ResourceId = $"opinion_{orgId}",
				Value = value
			});
		}

		[Fact]
		void build_uses_definition_target_role_for_opinion() {
			var world = new World();
			AddCharacter(world, "Prussia", "diplomat", "diplomacy_advisor");
			AddOpinion(world, "diplomat", "OrgA", 100);
			AddCharacter(world, "Prussia", "general", "military_advisor");
			AddOpinion(world, "general", "OrgA", 79);

			ExpressionContext context = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Prussia");

			Assert.Equal(79, context.Opinion);
		}

		[Fact]
		void build_returns_exact_threshold_for_target_advisor() {
			var world = new World();
			AddCharacter(world, "Prussia", "general", "military_advisor");
			AddOpinion(world, "general", "OrgA", 80);

			ExpressionContext context = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Prussia");

			Assert.Equal(80, context.Opinion);
		}

		[Fact]
		void build_defaults_opinion_to_zero_when_advisor_or_resource_is_missing() {
			var worldWithoutAdvisor = new World();
			ExpressionContext withoutAdvisor = CountryActionConditionContext.Build(
				worldWithoutAdvisor,
				Definition(),
				"OrgA",
				"Prussia");

			var worldWithoutOpinion = new World();
			AddCharacter(worldWithoutOpinion, "Prussia", "general", "military_advisor");
			ExpressionContext withoutOpinion = CountryActionConditionContext.Build(
				worldWithoutOpinion,
				Definition(),
				"OrgA",
				"Prussia");

			Assert.Equal(0, withoutAdvisor.Opinion);
			Assert.Equal(0, withoutOpinion.Opinion);
		}

		[Fact]
		void build_uses_current_advisor_after_replacement() {
			var world = new World();
			int oldAdvisor = AddCharacter(world, "Prussia", "old_general", "military_advisor");
			AddOpinion(world, "old_general", "OrgA", 90);

			ExpressionContext before = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Prussia");

			world.Destroy(oldAdvisor);
			AddCharacter(world, "Prussia", "new_general", "military_advisor");
			AddOpinion(world, "new_general", "OrgA", 40);

			ExpressionContext after = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Prussia");

			Assert.Equal(90, before.Opinion);
			Assert.Equal(40, after.Opinion);
		}

		[Fact]
		void build_sets_is_in_war_for_attacker_and_defender_only() {
			var world = new World();
			Wars.DeclareWar(world, "Prussia", "Austria", new DateTime(1880, 1, 1));

			ExpressionContext attacker = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Prussia");
			ExpressionContext defender = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Austria");
			ExpressionContext nonParticipant = CountryActionConditionContext.Build(
				world,
				Definition(),
				"OrgA",
				"Bavaria");

			Assert.Equal(1, attacker.IsInWar);
			Assert.Equal(1, defender.IsInWar);
			Assert.Equal(0, nonParticipant.IsInWar);
		}

		[Fact]
		void build_preserves_relation_card_target_evaluation() {
			var world = new World();
			int card = world.Create();
			world.Add(card, new RelationCardTarget {
				TargetCountryId = "Austria",
				Kind = RelationKind.Friend
			});

			ExpressionContext beforeRelation = CountryActionConditionContext.Build(
				world,
				Definition("diplomacy_advisor"),
				"OrgA",
				"Prussia",
				card);

			CountryRelations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			ExpressionContext afterRelation = CountryActionConditionContext.Build(
				world,
				Definition("diplomacy_advisor"),
				"OrgA",
				"Prussia",
				card);

			Assert.Equal(0, beforeRelation.GetCountryRelation("friend"));
			Assert.Equal(1, afterRelation.GetCountryRelation("friend"));
		}
	}
}
