using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryActionSystemTests {
		const string OrgId = "TestOrg";
		const string CountryId = "France";
		const string ActionId = "sphere_of_pressure";
		const string AdvisorActionId = "letter_of_commendation_diplomacy_advisor";
		const string CharId = "char_001";

		static (ActionConfig actionConfig, EffectConfig effectConfig) BuildConfig(
			string actionId = ActionId,
			double successRate = 1.0,
			int influenceAmount = 10,
			double goldCost = 50,
			string opinionSourceId = "",
			int opinionInitialValue = 5,
			int opinionDecayPerMonth = 1,
			ExpressionNode? conditionNode = null) {
			var influenceEffects = new List<ActionEffectDefinition>();
			if (influenceAmount > 0) {
				influenceEffects.Add(new InfluenceChangeEffectParams {
					EffectId = $"{actionId}_influence",
					EffectType = "InfluenceChange",
					Amount = influenceAmount
				});
			}
			if (!string.IsNullOrEmpty(opinionSourceId)) {
				influenceEffects.Add(new OpinionModifierEffectParams {
					EffectId = $"{actionId}_opinion",
					EffectType = "OpinionModifier",
					SourceId = opinionSourceId,
					InitialValue = opinionInitialValue,
					DecayPerMonth = opinionDecayPerMonth
				});
			}
			var effectIds = new List<string>();
			foreach (var e in influenceEffects) { effectIds.Add(e.EffectId); }

			var conditions = new List<ExpressionNode>();
			if (conditionNode != null) { conditions.Add(conditionNode); }

			var actionConfig = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = actionId,
						SuccessRateNode = new ExpressionNode { Type = "value", Value = successRate },
						Cost = goldCost > 0 ? new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = goldCost } } : new List<ActionCost>(),
						EffectIds = effectIds,
						Conditions = conditions
					}
				}
			};
			var effectConfig = new EffectConfig { Effects = influenceEffects };
			return (actionConfig, effectConfig);
		}

		static (World world, int goldEntity, int cardHandEntity) BuildWorld(double gold = 500) {
			var world = new World();

			int goldEntity = world.Create();
			world.Add(goldEntity, new ResourceOwner(OrgId));
			world.Add(goldEntity, new Resource { ResourceId = "gold", Value = gold });

			int cardHandEntity = world.Create();
			world.Add(cardHandEntity, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCharacterId = ""
			});
			world.Add(cardHandEntity, new InHand { SlotIndex = 0 });

			return (world, goldEntity, cardHandEntity);
		}

		static PlayCountryActionCommand MakeCmd(string actionId = ActionId, string targetCharId = "") {
			return new PlayCountryActionCommand {
				OrgId = OrgId,
				CountryId = CountryId,
				ActionId = actionId,
				TargetCharacterId = targetCharId
			};
		}

		static int CountInfluenceEffects(World world, string orgId, string countryId) {
			int total = 0;
			int[] req = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				InfluenceEffect[] effs = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effs[i].OrgId == orgId && effs[i].CountryId == countryId) { total += effs[i].Value; }
				}
			}
			return total;
		}

		static int CountHandCards(World world) {
			int count = 0;
			int[] req = { TypeId<CountryActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) { count += arch.Count; }
			return count;
		}

		[Fact]
		void play_returns_not_executed_if_no_gold() {
			var (world, goldEntity, _) = BuildWorld(gold: 10);
			var (config, effectConfig) = BuildConfig(goldCost: 50);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.False(result.Executed);
			Assert.Equal(10.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_deducts_gold_on_execution() {
			var (world, goldEntity, _) = BuildWorld(gold: 200);
			var (config, effectConfig) = BuildConfig(goldCost: 50);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.True(result.Executed);
			Assert.Equal(150.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_returns_not_executed_below_influence_threshold() {
			var (world, _, _) = BuildWorld();
			// Condition: influence >= 10
			var condition = new ExpressionNode {
				Type = "gte",
				Members = new List<ExpressionNode> {
					new ExpressionNode { Type = "influence" },
					new ExpressionNode { Type = "value", Value = 10 }
				}
			};
			var (config, effectConfig) = BuildConfig(conditionNode: condition);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.False(result.Executed);
		}

		[Fact]
		void play_allowed_when_conditions_met() {
			var (world, _, _) = BuildWorld();
			// Add influence so condition passes
			int inflEntity = world.Create();
			world.Add(inflEntity, new InfluenceEffect { OrgId = OrgId, CountryId = CountryId, Value = 10 });
			var condition = new ExpressionNode {
				Type = "gte",
				Members = new List<ExpressionNode> {
					new ExpressionNode { Type = "influence" },
					new ExpressionNode { Type = "value", Value = 10 }
				}
			};
			var (config, effectConfig) = BuildConfig(conditionNode: condition, influenceAmount: 0);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.True(result.Executed);
		}

		[Fact]
		void play_success_adds_influence() {
			var (world, _, _) = BuildWorld();
			var (config, effectConfig) = BuildConfig(successRate: 1.0, influenceAmount: 10);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.True(result.Success);
			Assert.Equal(10, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_failure_does_not_add_influence() {
			var (world, _, _) = BuildWorld();
			var (config, effectConfig) = BuildConfig(successRate: 0.0, influenceAmount: 10);
			var result = ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));
			Assert.False(result.Success);
			Assert.Equal(0, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_success_capped_at_pool_limit() {
			var (world, _, _) = BuildWorld();

			// Fill pool to 95
			int fillEntity = world.Create();
			world.Add(fillEntity, new InfluenceEffect { OrgId = OrgId, CountryId = CountryId, Value = 95 });

			var (config, effectConfig) = BuildConfig(successRate: 1.0, influenceAmount: 10);
			ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));

			// Should only add 5 to reach cap of 100
			Assert.Equal(100, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_success_adds_no_influence_when_pool_full() {
			var (world, _, _) = BuildWorld();

			int fillEntity = world.Create();
			world.Add(fillEntity, new InfluenceEffect { OrgId = OrgId, CountryId = CountryId, Value = 100 });

			var (config, effectConfig) = BuildConfig(successRate: 1.0, influenceAmount: 10);
			ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, effectConfig, DateTime.UtcNow, new Random(1));

			Assert.Equal(100, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_draws_from_eligible_deck_card() {
			var (world, _, _) = BuildWorld();

			int deckCard = world.Create();
			world.Add(deckCard, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = AdvisorActionId, TargetCharacterId = ""
			});

			var actionConfig = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = ActionId, SuccessRateNode = new ExpressionNode { Type = "value", Value = 1.0 }, Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50 } } },
					new ActionDefinition { ActionId = AdvisorActionId, SuccessRateNode = new ExpressionNode { Type = "value", Value = 1.0 }, Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50 } } }
				}
			};
			var effectConfig = new EffectConfig();
			ActionSystem.ProcessPlayCountryAction(world, MakeCmd(), actionConfig, effectConfig, DateTime.UtcNow, new Random(1));

			// Played card returns to deck (no cooldown) and both it and the advisor deck card are drawn
			Assert.Equal(2, CountHandCards(world));
		}

		[Fact]
		void play_success_adds_opinion_modifier_to_target_character() {
			var (world, _, _) = BuildWorld();

			int charEntity = world.Create();
			world.Add(charEntity, new Character { CharacterId = CharId });
			world.Add(charEntity, new CharacterOpinion {
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>>()
			});

			var (config, effectConfig) = BuildConfig(successRate: 1.0, opinionSourceId: "commendation", opinionInitialValue: 5, opinionDecayPerMonth: 0, influenceAmount: 0);
			ActionSystem.ProcessPlayCountryAction(
				world, MakeCmd(targetCharId: CharId), config, effectConfig, DateTime.UtcNow, new Random(1));

			ref CharacterOpinion opinion = ref world.Get<CharacterOpinion>(charEntity);
			Assert.True(opinion.ModifiersPerOrg.ContainsKey(OrgId));
			Assert.True(opinion.ModifiersPerOrg[OrgId].Count > 0);
			Assert.Equal("commendation", opinion.ModifiersPerOrg[OrgId][0].SourceId);
		}

		[Fact]
		void play_failure_does_not_add_opinion_modifier() {
			var (world, _, _) = BuildWorld();

			int charEntity = world.Create();
			world.Add(charEntity, new Character { CharacterId = CharId });
			world.Add(charEntity, new CharacterOpinion {
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>>()
			});

			var (config, effectConfig) = BuildConfig(successRate: 0.0, opinionSourceId: "commendation", influenceAmount: 0);
			ActionSystem.ProcessPlayCountryAction(
				world, MakeCmd(targetCharId: CharId), config, effectConfig, DateTime.UtcNow, new Random(1));

			ref CharacterOpinion opinion = ref world.Get<CharacterOpinion>(charEntity);
			bool hasModifier = opinion.ModifiersPerOrg != null && opinion.ModifiersPerOrg.ContainsKey(OrgId);
			Assert.False(hasModifier);
		}

	}
}
