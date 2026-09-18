using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Newtonsoft.Json;
using Xunit;

namespace GS.Game.Tests {
	public class DeclareWarCardTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		const string OrgId = "OrgA";
		const string AttackerId = "Prussia";
		const string DefenderId = "Austria";
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 1);

		static ActionConfig BuildActionConfig() {
			return TestActionConfig.Create()
				.HandSize("country", 3)
				.Action("stop_rivalry", chance: 1,
					cost: Gold(100),
					effectIds: new[] { "clear_rivalry_effect" },
					conditions: new[] { Expr.Gte("opinion", 80), RelationStillExists() })
				.Action("declare_war", chance: 1,
					cost: Gold(100),
					effectIds: new[] { "declare_war_effect" },
					conditions: new[] {
						Expr.Gte("targetMilitaryOpinion", 50),
						RelationStillExists(),
						Expr.Gte("neitherSideAtWar", 1)
					})
				.Build();
		}

		static ActionCost[] Gold(double amount) {
			return new[] { new ActionCost { ResourceId = "gold", Amount = amount } };
		}

		/// <summary>The rivalry these cards were created for must still exist when they are played.</summary>
		static ExpressionNode RelationStillExists() {
			return Expr.HasRelation("rival");
		}

		static int AddCountry(TestWorld world, string countryId, bool selected = false) {
			return world.Country(countryId, selected).Last;
		}

		static int AddRoleOpinion(TestWorld world, string countryId, string roleId, double opinion) {
			string characterId = $"{countryId}_{roleId}";
			return world
				.Character(characterId, countryId, roleId)
				.Opinion(characterId, opinion, OrgId)
				.Last;
		}

		static void AddGold(TestWorld world, double amount) {
			world.Resource(OrgId, "gold", amount);
		}

		static int AddDeclareWarCard(TestWorld world, bool inHand = false, bool readyToResolve = false) {
			world.Card("declare_war", slotIndex: inHand ? 0 : (int?)null, orgId: OrgId, countryId: AttackerId)
				.With(new RelationCardTarget { TargetCountryId = DefenderId, Kind = RelationKind.Rival });
			if (readyToResolve) {
				world.With(new CardUse { CountryId = AttackerId }).With(new ActionSucceeded());
			}
			return world.Last;
		}

		TestWorld BuildPlayableWorld(string roleId = "military_advisor", double opinion = 50, double gold = 100) {
			var world = TestWorld.Create().Countries(AttackerId, DefenderId, "Germany");
			AddGold(world, gold);
			AddRoleOpinion(world, AttackerId, roleId, opinion);
			_relations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);
			return world;
		}

		static int CountActionInstances(World world, string actionId, string targetCountryId) {
			int count = 0;
			int[] required = { TypeId<GameAction>.Value, TypeId<RelationCardTarget>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				RelationCardTarget[] targets = arch.GetColumn<RelationCardTarget>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == actionId && targets[i].TargetCountryId == targetCountryId) {
						count++;
					}
				}
			}
			return count;
		}

		[Fact]
		void expression_nodes_read_declare_war_gate_values() {
			var context = new ExpressionContext {
				TargetMilitaryOpinion = 63,
				NeitherSideAtWar = 1
			};

			Assert.Equal(63, ExpressionNode.Evaluate(
				new ExpressionNode { Type = "targetMilitaryOpinion" }, context));
			Assert.Equal(1, ExpressionNode.Evaluate(
				new ExpressionNode { Type = "neitherSideAtWar" }, context));
		}

		[Fact]
		void declare_war_effect_deserializes_to_typed_definition() {
			const string json = """
				{
				  "effects": [
				    {
				      "effectId": "declare_war_effect",
				      "effectType": "DeclareWar",
				      "nameKey": "effect.declare_war_effect.name",
				      "descKey": "effect.declare_war_effect.desc"
				    }
				  ]
				}
				""";

			var config = JsonConvert.DeserializeObject<EffectConfig>(json);

			Assert.NotNull(config);
			Assert.IsType<DeclareWarEffectParams>(config!.Find("declare_war_effect"));
		}

		[Fact]
		void declare_war_is_playable_when_declaring_country_military_advisor_meets_opinion_gate() {
			var world = BuildPlayableWorld("military_advisor");
			int card = AddDeclareWarCard(world, inHand: true);

			Assert.True(ActionPlayability.Evaluate(world, BuildActionConfig(), card, "declare_war", OrgId, AttackerId, _resources, _relations));
		}

		[Fact]
		void declare_war_is_unplayable_when_only_ruler_meets_opinion_gate() {
			var world = BuildPlayableWorld("ruler", opinion: 100);
			int card = AddDeclareWarCard(world, inHand: true);

			Assert.False(ActionPlayability.Evaluate(world, BuildActionConfig(), card, "declare_war", OrgId, AttackerId, _resources, _relations));
		}

		[Fact]
		void declare_war_ignores_rival_country_character_opinion() {
			var world = TestWorld.Create();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			AddGold(world, 100);
			AddRoleOpinion(world, AttackerId, "military_advisor", 0);
			AddRoleOpinion(world, DefenderId, "military_advisor", 100);
			_relations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);
			int card = AddDeclareWarCard(world, inHand: true);

			Assert.False(ActionPlayability.Evaluate(world, BuildActionConfig(), card, "declare_war", OrgId, AttackerId, _resources, _relations));
		}

		[Fact]
		void declare_war_is_unplayable_when_opinion_relation_cost_or_war_gate_fails() {
			var lowOpinionWorld = BuildPlayableWorld(opinion: 49);
			int lowOpinionCard = AddDeclareWarCard(lowOpinionWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(lowOpinionWorld, BuildActionConfig(), lowOpinionCard, "declare_war", OrgId, AttackerId, _resources, _relations));

			var deadRelationWorld = BuildPlayableWorld();
			_relations.RemoveRelation(deadRelationWorld, AttackerId, DefenderId);
			int deadRelationCard = AddDeclareWarCard(deadRelationWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(deadRelationWorld, BuildActionConfig(), deadRelationCard, "declare_war", OrgId, AttackerId, _resources, _relations));

			var unaffordableWorld = BuildPlayableWorld(gold: 99);
			int unaffordableCard = AddDeclareWarCard(unaffordableWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(unaffordableWorld, BuildActionConfig(), unaffordableCard, "declare_war", OrgId, AttackerId, _resources, _relations));

			var attackerAtWarWorld = BuildPlayableWorld();
			Wars.DeclareWar(attackerAtWarWorld, _resources, AttackerId, "Germany", CurrentTime);
			int attackerAtWarCard = AddDeclareWarCard(attackerAtWarWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(attackerAtWarWorld, BuildActionConfig(), attackerAtWarCard, "declare_war", OrgId, AttackerId, _resources, _relations));

			var defenderAtWarWorld = BuildPlayableWorld();
			Wars.DeclareWar(defenderAtWarWorld, _resources, DefenderId, "Germany", CurrentTime);
			int defenderAtWarCard = AddDeclareWarCard(defenderAtWarWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(defenderAtWarWorld, BuildActionConfig(), defenderAtWarCard, "declare_war", OrgId, AttackerId, _resources, _relations));
		}

		[Fact]
		void draw_ignores_declaring_country_opinion_and_war_gates() {
			var world = BuildPlayableWorld(opinion: 49);
			int opinionResource = _resources.FindEntity(world, $"{AttackerId}_military_advisor", $"opinion_{OrgId}");
			int card = AddDeclareWarCard(world);
			world.Deck(OrgId, handSize: 1);

			DrawAndReceive(world);
			Assert.True(world.Has<CardInHand>(card));

			world.Get<Resource>(opinionResource).Value = 50;
			world.Remove<CardInHand>(card);
			DrawAndReceive(world);
			Assert.True(world.Has<CardInHand>(card));

			world.Remove<CardInHand>(card);
			Wars.DeclareWar(world, _resources, AttackerId, "Germany", CurrentTime);
			DrawAndReceive(world);
			Assert.True(world.Has<CardInHand>(card));
		}

		static void DrawAndReceive(World world) {
			DrawCardSystem.Update(
				world,
				BuildActionConfig(),
				new EffectConfig(),
				new Random(1),
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = OrgId } }),
				Array.Empty<DiscardCardResult>(),
				new CountryRelations(),
				OrgId);
			ReceiveCardSystem.Update(
				world,
				new ReadCommands<ReceiveCardCommand>(new[] {
					new ReceiveCardCommand { OrgId = OrgId, ChoiceIndex = 0 }
				}));
		}

		[Fact]
		void relation_sync_creates_stop_rivalry_and_declare_war_instances_per_rival() {
			var world = TestWorld.Create();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			AddCountry(world, "Spain");
			world.Entity()
				.With(new CardDeck { OrgId = OrgId })
				.With(new CardOwnerType(CardOwnerKind.Country));
			_relations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);
			_relations.SetRelation(world, AttackerId, "Spain", RelationKind.Rival);

			RelationCardSyncSystem.Update(world, _relations, BuildActionConfig());

			Assert.Equal(1, CountActionInstances(world, "stop_rivalry", DefenderId));
			Assert.Equal(1, CountActionInstances(world, "declare_war", DefenderId));
			Assert.Equal(1, CountActionInstances(world, "stop_rivalry", "Spain"));
			Assert.Equal(1, CountActionInstances(world, "declare_war", "Spain"));
		}

		[Fact]
		void relation_sync_skips_declare_war_when_deck_copies_is_zero() {
			var world = TestWorld.Create();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			world.Entity()
				.With(new CardDeck { OrgId = OrgId })
				.With(new CardOwnerType(CardOwnerKind.Country));
			_relations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);

			var config = BuildActionConfig();
			config.Find("declare_war")!.Chance = 0;
			RelationCardSyncSystem.Update(world, _relations, config);

			Assert.Equal(0, CountActionInstances(world, "declare_war", DefenderId));
		}

		[Fact]
		void declare_war_effect_creates_war_and_transient_log_event_only_on_success() {
			var actionConfig = BuildActionConfig();
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DeclareWarEffectParams { EffectId = "declare_war_effect", EffectType = "DeclareWar" }
				}
			};
			var world = BuildPlayableWorld();
			AddDeclareWarCard(world, readyToResolve: true);

			CreateActionEffectSystem.Update(world, actionConfig, effectConfig, CurrentTime, new Random(1), new GameSettings(), new ProvinceTopology(new ProvinceConfig()), new Dictionary<string, (double Lon, double Lat)>(), 100, _resources);

			Assert.True(Wars.IsInWar(world, AttackerId));
			Assert.True(Wars.IsInWar(world, DefenderId));
			Assert.True(world.Count<WarDeclaredApplied>() > 0);

			CleanupEffectNotificationsSystem.UpdateActionEffects(world);
			Assert.False(world.Count<WarDeclaredApplied>() > 0);

			var blockedWorld = BuildPlayableWorld();
			Wars.DeclareWar(blockedWorld, _resources, DefenderId, "Germany", CurrentTime);
			AddDeclareWarCard(blockedWorld, readyToResolve: true);

			CreateActionEffectSystem.Update(blockedWorld, actionConfig, effectConfig, CurrentTime.AddDays(1), new Random(1), new GameSettings(), new ProvinceTopology(new ProvinceConfig()), new Dictionary<string, (double Lon, double Lat)>(), 100, _resources);

			Assert.False(Wars.IsInWar(blockedWorld, AttackerId));
			Assert.False(blockedWorld.Count<WarDeclaredApplied>() > 0);
		}

		[Fact]
		void visual_state_reports_distinct_opinion_and_already_at_war_reasons() {
			var world = BuildPlayableWorld(opinion: 49);
			world.Add(FindCountryEntity(world, AttackerId), new IsSelected());
			int card = AddDeclareWarCard(world, inHand: true);
			world.GameTime(CurrentTime).Locale().Org(OrgId);
			var probe = new VisualStateProbe(BuildActionConfig(), _resources, _relations);

			VisualState state = probe.Update(world).State;

			ActionCardEntry entry = Assert.Single(state.SelectedCountry.CountryActions.Hand.Where(e => e.ActionId == "declare_war"));
			Assert.Equal("insufficient_target_opinion", entry.UnplayableReason);
			Assert.Equal(5, entry.Conditions.Count);
			Assert.False(entry.Conditions[0].Passed);
			Assert.Contains("targetMilitaryOpinion", entry.Conditions[0].Label);
			Assert.True(entry.Conditions[1].Passed);
			Assert.True(entry.Conditions[2].Passed);
			Assert.All(entry.Conditions.Skip(3), condition => Assert.True(condition.Passed));

			int opinionResource = _resources.FindEntity(world, $"{AttackerId}_military_advisor", $"opinion_{OrgId}");
			Assert.True(opinionResource >= 0);
			_resources.TryUpdate(world, $"{AttackerId}_military_advisor", $"opinion_{OrgId}", 50, out _);
			Wars.DeclareWar(world, _resources, AttackerId, "Germany", CurrentTime);
			probe.Update(world);

			entry = Assert.Single(state.SelectedCountry.CountryActions.Hand.Where(e => e.ActionId == "declare_war"));
			Assert.Equal("already_at_war", entry.UnplayableReason);
			Assert.Equal(DefenderId, entry.TargetCountryId);
			Assert.True(entry.Conditions[0].Passed);
			Assert.True(entry.Conditions[1].Passed);
			Assert.False(entry.Conditions[2].Passed);
			Assert.True(world.Has<CardInHand>(card));
		}

		[Fact]
		void war_event_produces_one_game_log_entry_with_attacker_and_defender() {
			TestWorld world = TestWorld.Create()
				.Countries(AttackerId, DefenderId)
				.GameTime(CurrentTime)
				.Locale()
				.Org(OrgId)
				.Entity().With(new WarDeclaredApplied {
					OrgId = OrgId,
					CountryId = AttackerId,
					DefenderCountryId = DefenderId
				});
			var probe = new VisualStateProbe(resources: _resources, relations: _relations);

			VisualState state = probe.Update(world).State;

			GameLogEntry entry = Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.War, entry.Kind);
			Assert.Equal(OrgId, entry.OrgId);
			Assert.Equal(AttackerId, entry.CountryId);
			Assert.Equal(DefenderId, entry.TargetCountryId);

			CleanupEffectNotificationsSystem.UpdateActionEffects(world);
			probe.Update(world);
			Assert.Single(state.GameLog.Entries);
		}

		static int FindCountryEntity(TestWorld world, string countryId) {
			int[] required = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) {
					if (countries[i].CountryId == countryId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
