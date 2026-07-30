using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Newtonsoft.Json;
using Xunit;

namespace GS.Game.Tests {
	public class DeclareWarCardTests {
		const string OrgId = "OrgA";
		const string AttackerId = "Prussia";
		const string DefenderId = "Austria";
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 1);

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "declare_war",
						OwnerType = "country",
						DeckCopies = 0,
						Conditions = new List<ExpressionNode> {
							Condition("targetRulerOrMilitaryOpinion", 50),
							Condition("relationStillExists", 1),
							Condition("neitherSideAtWar", 1)
						},
						Cost = new List<ActionCost> {
							new ActionCost { ResourceId = "gold", Amount = 100 }
						},
						EffectIds = new List<string> { "declare_war_effect" }
					}
				}
			};
		}

		static ExpressionNode Condition(string fieldType, double value) {
			return new ExpressionNode {
				Type = "gte",
				Members = new List<ExpressionNode> {
					new ExpressionNode { Type = fieldType },
					new ExpressionNode { Type = "value", Value = value }
				}
			};
		}

		static int AddCountry(World world, string countryId, bool selected = false) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			if (selected) {
				world.Add(entity, new IsSelected());
			}
			return entity;
		}

		static int AddRoleOpinion(World world, string countryId, string roleId, double opinion) {
			string characterId = $"{countryId}_{roleId}";
			int characterEntity = world.Create();
			world.Add(characterEntity, new Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});
			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(resourceEntity, new Resource { ResourceId = $"opinion_{OrgId}", Value = opinion });
			return resourceEntity;
		}

		static void AddGold(World world, double amount) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(OrgId));
			world.Add(entity, new Resource { ResourceId = "gold", Value = amount });
		}

		static int AddDeclareWarCard(World world, bool inHand = false, bool readyToResolve = false) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = "declare_war" });
			world.Add(entity, new OrgContext { OrgId = OrgId });
			world.Add(entity, new CountryContext { CountryId = AttackerId });
			world.Add(entity, new RelationCardTarget { TargetCountryId = DefenderId, Kind = RelationKind.Rival });
			if (inHand) {
				world.Add(entity, new CardInHand { SlotIndex = 0 });
			}
			if (readyToResolve) {
				world.Add(entity, new CardUse());
				world.Add(entity, new ActionSucceeded());
			}
			return entity;
		}

		static World BuildPlayableWorld(string roleId = "ruler", double opinion = 50, double gold = 100) {
			var world = new World();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			AddCountry(world, "Germany");
			AddGold(world, gold);
			AddRoleOpinion(world, DefenderId, roleId, opinion);
			CountryRelations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);
			return world;
		}

		static bool HasComponent<T>(World world) where T : struct {
			int[] required = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return true;
				}
			}
			return false;
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
				TargetRulerOrMilitaryOpinion = 63,
				NeitherSideAtWar = 1
			};

			Assert.Equal(63, ExpressionNode.Evaluate(
				new ExpressionNode { Type = "targetRulerOrMilitaryOpinion" }, context));
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

		[Theory]
		[InlineData("ruler")]
		[InlineData("military_advisor")]
		void declare_war_is_playable_when_either_target_role_meets_opinion_gate(string roleId) {
			var world = BuildPlayableWorld(roleId);
			int card = AddDeclareWarCard(world, inHand: true);

			Assert.True(ActionPlayability.Evaluate(world, BuildActionConfig(), card, "declare_war", OrgId, AttackerId));
		}

		[Fact]
		void declare_war_is_unplayable_when_opinion_relation_cost_or_war_gate_fails() {
			var lowOpinionWorld = BuildPlayableWorld(opinion: 49);
			int lowOpinionCard = AddDeclareWarCard(lowOpinionWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(lowOpinionWorld, BuildActionConfig(), lowOpinionCard, "declare_war", OrgId, AttackerId));

			var deadRelationWorld = BuildPlayableWorld();
			CountryRelations.RemoveRelation(deadRelationWorld, AttackerId, DefenderId);
			int deadRelationCard = AddDeclareWarCard(deadRelationWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(deadRelationWorld, BuildActionConfig(), deadRelationCard, "declare_war", OrgId, AttackerId));

			var unaffordableWorld = BuildPlayableWorld(gold: 99);
			int unaffordableCard = AddDeclareWarCard(unaffordableWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(unaffordableWorld, BuildActionConfig(), unaffordableCard, "declare_war", OrgId, AttackerId));

			var attackerAtWarWorld = BuildPlayableWorld();
			Wars.DeclareWar(attackerAtWarWorld, AttackerId, "Germany", CurrentTime);
			int attackerAtWarCard = AddDeclareWarCard(attackerAtWarWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(attackerAtWarWorld, BuildActionConfig(), attackerAtWarCard, "declare_war", OrgId, AttackerId));

			var defenderAtWarWorld = BuildPlayableWorld();
			Wars.DeclareWar(defenderAtWarWorld, DefenderId, "Germany", CurrentTime);
			int defenderAtWarCard = AddDeclareWarCard(defenderAtWarWorld, inHand: true);
			Assert.False(ActionPlayability.Evaluate(defenderAtWarWorld, BuildActionConfig(), defenderAtWarCard, "declare_war", OrgId, AttackerId));
		}

		[Fact]
		void draw_rechecks_target_opinion_and_war_gates() {
			var world = BuildPlayableWorld(opinion: 49);
			int opinionResource = ActionPlayability.FindResourceEntity(world, $"{DefenderId}_ruler", $"opinion_{OrgId}");
			int card = AddDeclareWarCard(world);
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = OrgId, CountryId = AttackerId });
			world.Add(deck, new CardDraw { Count = 1 });

			DrawCardSystem.Update(world, BuildActionConfig(), new Random(1));
			Assert.False(world.Has<CardInHand>(card));

			world.Get<Resource>(opinionResource).Value = 50;
			world.Add(deck, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, BuildActionConfig(), new Random(1));
			Assert.True(world.Has<CardInHand>(card));

			world.Remove<CardInHand>(card);
			Wars.DeclareWar(world, AttackerId, "Germany", CurrentTime);
			world.Add(deck, new CardDraw { Count = 1 });
			DrawCardSystem.Update(world, BuildActionConfig(), new Random(1));
			Assert.False(world.Has<CardInHand>(card));
		}

		[Fact]
		void relation_sync_creates_stop_rivalry_and_declare_war_instances_per_rival() {
			var world = new World();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			AddCountry(world, "Spain");
			int deck = world.Create();
			world.Add(deck, new CardDeck { OrgId = OrgId, CountryId = AttackerId });
			CountryRelations.SetRelation(world, AttackerId, DefenderId, RelationKind.Rival);
			CountryRelations.SetRelation(world, AttackerId, "Spain", RelationKind.Rival);

			RelationCardSyncSystem.Update(world, BuildActionConfig());

			Assert.Equal(1, CountActionInstances(world, "stop_rivalry", DefenderId));
			Assert.Equal(1, CountActionInstances(world, "declare_war", DefenderId));
			Assert.Equal(1, CountActionInstances(world, "stop_rivalry", "Spain"));
			Assert.Equal(1, CountActionInstances(world, "declare_war", "Spain"));
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

			CreateActionEffectSystem.Update(world, actionConfig, effectConfig, CurrentTime);

			Assert.True(Wars.IsInWar(world, AttackerId));
			Assert.True(Wars.IsInWar(world, DefenderId));
			Assert.True(HasComponent<WarDeclaredApplied>(world));

			CleanupEffectNotificationsSystem.UpdateActionEffects(world);
			Assert.False(HasComponent<WarDeclaredApplied>(world));

			var blockedWorld = BuildPlayableWorld();
			Wars.DeclareWar(blockedWorld, DefenderId, "Germany", CurrentTime);
			AddDeclareWarCard(blockedWorld, readyToResolve: true);

			CreateActionEffectSystem.Update(blockedWorld, actionConfig, effectConfig, CurrentTime.AddDays(1));

			Assert.False(Wars.IsInWar(blockedWorld, AttackerId));
			Assert.False(HasComponent<WarDeclaredApplied>(blockedWorld));
		}

		[Fact]
		void visual_state_reports_distinct_opinion_and_already_at_war_reasons() {
			var world = BuildPlayableWorld(opinion: 49);
			int attackerEntity = FindCountryEntity(world, AttackerId);
			world.Add(attackerEntity, new IsSelected());
			int card = AddDeclareWarCard(world, inHand: true);
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = CurrentTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = OrgId, DisplayName = OrgId });
			var state = new VisualState();
			var converter = new VisualStateConverter(state, BuildActionConfig());

			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			ActionCardEntry entry = Assert.Single(state.SelectedCountry.CountryActions.Hand.Where(e => e.ActionId == "declare_war"));
			Assert.Equal("insufficient_target_opinion", entry.UnplayableReason);
			Assert.Equal(3, entry.Conditions.Count);
			Assert.False(entry.Conditions[0].Passed);
			Assert.Contains("targetRulerOrMilitaryOpinion", entry.Conditions[0].Label);
			Assert.True(entry.Conditions[1].Passed);
			Assert.True(entry.Conditions[2].Passed);

			int opinionResource = ActionPlayability.FindResourceEntity(world, $"{DefenderId}_ruler", $"opinion_{OrgId}");
			world.Get<Resource>(opinionResource).Value = 50;
			Wars.DeclareWar(world, AttackerId, "Germany", CurrentTime);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

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
			var world = new World();
			AddCountry(world, AttackerId);
			AddCountry(world, DefenderId);
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = CurrentTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = OrgId, DisplayName = OrgId });
			int eventEntity = world.Create();
			world.Add(eventEntity, new WarDeclaredApplied {
				OrgId = OrgId,
				CountryId = AttackerId,
				DefenderCountryId = DefenderId
			});
			var state = new VisualState();
			var converter = new VisualStateConverter(state);

			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			GameLogEntry entry = Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.War, entry.Kind);
			Assert.Equal(OrgId, entry.OrgId);
			Assert.Equal(AttackerId, entry.CountryId);
			Assert.Equal(DefenderId, entry.TargetCountryId);

			CleanupEffectNotificationsSystem.UpdateActionEffects(world);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);
			Assert.Single(state.GameLog.Entries);
		}

		static int FindCountryEntity(World world, string countryId) {
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
